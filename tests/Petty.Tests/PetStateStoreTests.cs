using Petty.Core.Domain;
using Petty.Core.Persistence;

namespace Petty.Tests;

public sealed class PetStateStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public PetStateStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "petty-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "state.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    private static PetSnapshot Sample(string name = "Muffin", double satiety = 70, double happiness = 60, double food = 40) => new()
    {
        PetName = name,
        Satiety = satiety,
        Happiness = happiness,
        BowlFood = food,
        BowlRelativeX = 0.42,
        BowlBottomOffset = 88,
        DisplayDeviceName = "DISPLAY2",
        CreatedUtc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero),
        LastUpdatedUtc = new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void SaveAndLoadRoundTripsEverything()
    {
        var store = new PetStateStore(_path);

        store.Save(Sample());
        var result = store.Load();

        Assert.Equal(LoadStatus.Loaded, result.Status);
        var snapshot = Assert.IsType<PetSnapshot>(result.Snapshot);
        Assert.Equal("Muffin", snapshot.PetName);
        Assert.Equal(70, snapshot.Satiety, 3);
        Assert.Equal(60, snapshot.Happiness, 3);
        Assert.Equal(40, snapshot.BowlFood, 3);
        Assert.Equal(0.42, snapshot.BowlRelativeX, 3);
        Assert.Equal(88, snapshot.BowlBottomOffset, 3);
        Assert.Equal("DISPLAY2", snapshot.DisplayDeviceName);
        Assert.Equal(PetSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
    }

    [Fact]
    public void SaveLeavesNoTemporaryFile()
    {
        var store = new PetStateStore(_path);
        store.Save(Sample());

        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + PetStateStore.TempSuffix));
    }

    [Fact]
    public void SecondSaveCreatesBackupAndCorruptPrimaryIsRecovered()
    {
        var store = new PetStateStore(_path);
        store.Save(Sample("First"));
        store.Save(Sample("Second"));
        Assert.True(File.Exists(store.BackupPath));

        File.WriteAllText(_path, "{ not valid json ");

        var result = store.Load();

        Assert.Equal(LoadStatus.RecoveredFromBackup, result.Status);
        Assert.Equal("First", result.Snapshot!.PetName);
    }

    [Fact]
    public void MissingFilesReportMissing()
    {
        var store = new PetStateStore(_path);
        var result = store.Load();

        Assert.Equal(LoadStatus.Missing, result.Status);
        Assert.False(result.HasPet);
    }

    [Fact]
    public void UnrecoverableCorruptionIsReported()
    {
        File.WriteAllText(_path, "garbage");
        File.WriteAllText(_path + PetStateStore.BackupSuffix, "also garbage");

        var result = new PetStateStore(_path).Load();

        Assert.Equal(LoadStatus.Corrupt, result.Status);
        Assert.False(result.HasPet);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void NewerSchemaIsRejected()
    {
        File.WriteAllText(_path, """
            { "schemaVersion": 99, "petName": "Future" }
            """);

        var result = new PetStateStore(_path).Load();

        Assert.Equal(LoadStatus.UnsupportedVersion, result.Status);
        Assert.False(result.HasPet);
    }

    [Fact]
    public void NewerSchemaIsPreservedEvenWithValidBackupAndChangedFieldTypes()
    {
        var store = new PetStateStore(_path);
        store.Save(Sample("Backup"));
        File.Copy(_path, store.BackupPath);
        const string future = "{ \"schemaVersion\": 99, \"petName\": { } }";
        File.WriteAllText(_path, future);

        Assert.Equal(LoadStatus.UnsupportedVersion, store.Load().Status);
        Assert.Throws<InvalidOperationException>(() => store.Save(Sample()));
        Assert.Equal(future, File.ReadAllText(_path));
        Assert.Equal("Backup", new PetStateStore(store.BackupPath).Load().Snapshot!.PetName);
    }

    [Fact]
    public void UnsupportedBackupIsPreservedWhenPrimaryIsMissing()
    {
        var store = new PetStateStore(_path);
        const string future = "{ \"schemaVersion\": 99 }";
        File.WriteAllText(store.BackupPath, future);

        Assert.Equal(LoadStatus.UnsupportedVersion, store.Load().Status);
        Assert.Throws<InvalidOperationException>(() => store.Save(Sample()));
        Assert.Equal(future, File.ReadAllText(store.BackupPath));
    }

    [Fact]
    public void InvalidValuesAreRepairedOnLoad()
    {
        File.WriteAllText(_path, """
            {
              "schemaVersion": 1,
              "petName": "   ",
              "satiety": 500,
              "happiness": -20,
              "bowlFood": 9999,
              "bowlRelativeX": 5,
              "bowlBottomOffset": -30
            }
            """);

        var result = new PetStateStore(_path).Load();

        Assert.Equal(LoadStatus.Loaded, result.Status);
        var snapshot = result.Snapshot!;
        Assert.Equal("Kitty", snapshot.PetName);
        Assert.Equal(100, snapshot.Satiety);
        Assert.Equal(0, snapshot.Happiness);
        Assert.Equal(PetTuning.Default.BowlCapacity, snapshot.BowlFood);
        Assert.InRange(snapshot.BowlRelativeX, BowlPlacement.MinRelativeX, BowlPlacement.MaxRelativeX);
        Assert.Equal(0, snapshot.BowlBottomOffset);
    }

    [Fact]
    public void InvalidNameIsReplacedWithTheDefault()
    {
        File.WriteAllText(_path, """
            { "schemaVersion": 1, "petName": "Bad\u0001Name" }
            """);

        var result = new PetStateStore(_path).Load();

        Assert.Equal(LoadStatus.Loaded, result.Status);
        Assert.Equal(PetNameRules.DefaultName, result.Snapshot!.PetName);
    }

    [Fact]
    public void DeleteRemovesStateAndBackup()
    {
        var store = new PetStateStore(_path);
        store.Save(Sample());
        store.Save(Sample());

        store.Delete();

        Assert.False(File.Exists(_path));
        Assert.False(File.Exists(store.BackupPath));
        Assert.Equal(LoadStatus.Missing, store.Load().Status);
    }
}
