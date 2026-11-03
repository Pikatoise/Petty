using System.Text.Json;

namespace Petty.Core.Preferences;

public sealed record UserPreferences(string? Language = null, bool Autostart = true, bool TutorialCompleted = false)
{
    public UserPreferences Sanitized() => this with
    {
        Language = Language?.Trim().ToLowerInvariant() is "ru" or "en" ? Language.Trim().ToLowerInvariant() : null,
    };

    public string EffectiveLanguage(string osLanguage) => Language ??
        (osLanguage.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en");
}

public sealed class PreferencesStore(string path)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public UserPreferences Load(Action<string>? reportError = null)
    {
        if (!File.Exists(path))
        {
            return new();
        }

        try
        {
            string json = File.ReadAllText(path);
            UserPreferences preferences = (JsonSerializer.Deserialize<UserPreferences>(json, Options) ?? new()).Sanitized();
            using var document = JsonDocument.Parse(json);
            // Preferences from older releases belong to existing users, who do not need onboarding.
            bool legacy = document.RootElement.ValueKind == JsonValueKind.Object &&
                !document.RootElement.EnumerateObject().Any(property =>
                    property.Name.Equals("tutorialCompleted", StringComparison.OrdinalIgnoreCase));
            return legacy ? preferences with
            {
                TutorialCompleted = true
            } : preferences;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            reportError?.Invoke($"Could not read preferences: {ex.Message}");
            return new();
        }
    }

    public void Save(UserPreferences preferences)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, preferences.Sanitized(), Options);
                stream.Flush(true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
