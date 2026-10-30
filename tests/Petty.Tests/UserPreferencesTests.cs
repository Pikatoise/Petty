using Petty.Core.Preferences;

namespace Petty.Tests;

public class UserPreferencesTests
{
    [Fact]
    public void UsesSystemLanguageWithEnglishFallbackAndAutostartEnabled()
    {
        var preferences = new UserPreferences();
        Assert.Equal("ru", preferences.EffectiveLanguage("ru-RU"));
        Assert.Equal("en", preferences.EffectiveLanguage("de-DE"));
        Assert.True(preferences.Autostart);
        Assert.Equal("en", new UserPreferences("en").EffectiveLanguage("ru-RU"));
    }

    [Fact]
    public void PreferencesRoundTripAndCorruptFileDoesNotTouchPetState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "petty-preferences-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "preferences.json");
            var petPath = Path.Combine(directory, "pet-state.json");
            File.WriteAllText(petPath, "existing pet");
            var store = new PreferencesStore(path);
            Assert.True(store.Load().Autostart);
            store.Save(new UserPreferences("RU", false));
            Assert.Equal(new UserPreferences("ru", false), store.Load());
            File.WriteAllText(path, "{ invalid");
            var errors = new List<string>();
            Assert.Equal(new UserPreferences(), store.Load(errors.Add));
            Assert.Single(errors);
            Assert.Equal("existing pet", File.ReadAllText(petPath));
            store.Save(new UserPreferences("unsupported"));
            Assert.Null(store.Load().Language);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
