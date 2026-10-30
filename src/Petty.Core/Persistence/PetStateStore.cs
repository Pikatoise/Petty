using System.Text.Json;
using System.Text.Json.Serialization;
using Petty.Core.Domain;

namespace Petty.Core.Persistence;

public enum LoadStatus
{
    Loaded,
    RecoveredFromBackup,
    Missing,
    Corrupt,
    UnsupportedVersion,
}

public sealed record LoadResult(LoadStatus Status, PetSnapshot? Snapshot, string? Message)
{
    public bool HasPet => Snapshot is not null;

    public static LoadResult Missing() => new(LoadStatus.Missing, null, "No saved pet state found.");
}

/// <summary>
/// Versioned JSON persistence with atomic replace and a single backup file.
/// A half-written state file is never left behind: the new content is written to
/// a temp file, flushed and then swapped in with File.Replace.
/// </summary>
public sealed class PetStateStore
{
    public const string BackupSuffix = ".bak";
    public const string TempSuffix = ".tmp";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true,
    };

    private enum ReadOutcome
    {
        Ok,
        NotFound,
        Corrupt,
        Unsupported,
    }

    private readonly object _gate = new();
    private readonly PetTuning _tuning;

    public PetStateStore(string filePath, PetTuning? tuning = null)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A file path is required.", nameof(filePath));
        FilePath = Path.GetFullPath(filePath);
        _tuning = tuning ?? PetTuning.Default;
    }

    public string FilePath { get; }

    public string BackupPath => FilePath + BackupSuffix;

    public bool Exists => File.Exists(FilePath);

    public LoadResult Load()
    {
        lock (_gate)
        {
            var outcome = TryRead(FilePath, out var snapshot, out var error);

            if (outcome is ReadOutcome.Ok)
                return new LoadResult(LoadStatus.Loaded, snapshot, null);

            if (outcome is ReadOutcome.Unsupported)
                return new LoadResult(LoadStatus.UnsupportedVersion, null, error);

            if (outcome is ReadOutcome.NotFound && !File.Exists(BackupPath))
                return LoadResult.Missing();

            var backupOutcome = TryRead(BackupPath, out var backup, out var backupError);
            if (backupOutcome is ReadOutcome.Ok)
                return new LoadResult(LoadStatus.RecoveredFromBackup, backup, error + " Restored the backup instead.");
            if (backupOutcome is ReadOutcome.Unsupported)
                return new LoadResult(LoadStatus.UnsupportedVersion, null, backupError);

            return outcome switch
            {
                ReadOutcome.Unsupported => new LoadResult(LoadStatus.UnsupportedVersion, null, error),
                ReadOutcome.NotFound => LoadResult.Missing(),
                _ => new LoadResult(LoadStatus.Corrupt, null, error),
            };
        }
    }

    public void Save(PetSnapshot snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

        lock (_gate)
        {
            // File.Replace also overwrites the backup, so protect both copies.
            foreach (var path in new[] { FilePath, BackupPath })
                if (TryRead(path, out _, out var error) is ReadOutcome.Unsupported)
                    throw new InvalidOperationException(error);

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var normalized = snapshot.Normalized(_tuning);
            normalized.CreatedUtc = snapshot.CreatedUtc;
            normalized.LastUpdatedUtc = snapshot.LastUpdatedUtc;

            var tempPath = FilePath + TempSuffix;
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, normalized, JsonOptions);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(FilePath))
                    File.Replace(tempPath, FilePath, BackupPath, ignoreMetadataErrors: true);
                else
                    File.Move(tempPath, FilePath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch (IOException) { /* best effort cleanup */ }
                }
            }
        }
    }

    public void Delete()
    {
        lock (_gate)
        {
            TryDelete(FilePath);
            TryDelete(BackupPath);
        }
    }

    private ReadOutcome TryRead(string path, out PetSnapshot? snapshot, out string? error)
    {
        snapshot = null;
        error = null;

        if (!File.Exists(path)) return ReadOutcome.NotFound;

        try
        {
            var json = File.ReadAllText(path);
            // Inspect the version before deserializing: future schemas may change field types.
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var version) && version > PetSnapshot.CurrentSchemaVersion)
                    {
                        error = $"State file schema v{version} is newer than supported v{PetSnapshot.CurrentSchemaVersion}.";
                        return ReadOutcome.Unsupported;
                    }
                }
            }
            var parsed = JsonSerializer.Deserialize<PetSnapshot>(json, JsonOptions);

            if (parsed is null)
            {
                error = $"State file '{Path.GetFileName(path)}' is empty.";
                return ReadOutcome.Corrupt;
            }

            if (parsed.SchemaVersion > PetSnapshot.CurrentSchemaVersion)
            {
                error = $"State file schema v{parsed.SchemaVersion} is newer than supported v{PetSnapshot.CurrentSchemaVersion}.";
                return ReadOutcome.Unsupported;
            }

            if (parsed.SchemaVersion < 1)
            {
                error = $"State file schema v{parsed.SchemaVersion} is not supported.";
                return ReadOutcome.Unsupported;
            }

            snapshot = parsed.Normalized(_tuning);
            return ReadOutcome.Ok;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = $"Could not read state file '{Path.GetFileName(path)}': {ex.Message}";
            return ReadOutcome.Corrupt;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Deleting state is best effort; a leftover file is harmless.
        }
    }
}
