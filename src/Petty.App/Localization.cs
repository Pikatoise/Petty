using Petty.Core.Preferences;
using System.Globalization;
using System.Windows;

namespace Petty.App;

internal sealed class Localization
{
    private readonly PreferencesStore _store;
    private ResourceDictionary? _strings;

    public Localization(PreferencesStore store, UserPreferences preferences)
    {
        _store = store;
        Preferences = preferences;
        Apply();
    }

    public UserPreferences Preferences { get; private set; }
    public event EventHandler? LanguageChanged;
    public event EventHandler? AutostartChanged;

    public static string Text(string key) => (string)Application.Current.FindResource(key);

    public static string BreedName(string breedId) =>
        Application.Current.TryFindResource("Breed-" + breedId) as string
        ?? (breedId.Equals("cat", StringComparison.OrdinalIgnoreCase) ? Text("BreedCat") : breedId.Replace('-', ' '));

    public static string SpeciesName(string speciesId) => Text(speciesId == "dog" ? "SpeciesDog" : "SpeciesCat");

    public void SetAutostart(bool enabled)
    {
        var updated = Preferences with { Autostart = enabled };
        _store.Save(updated);
        Preferences = updated;
        AutostartChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetLanguage(string? language)
    {
        var updated = (Preferences with { Language = language }).Sanitized();
        _store.Save(updated);
        Preferences = updated;
        Apply();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Apply()
    {
        var language = Preferences.EffectiveLanguage(CultureInfo.InstalledUICulture.Name);
        var resources = Application.Current.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = new Uri($"/Petty;component/Resources/Strings.{language}.xaml", UriKind.Relative) };
        if (_strings is not null) resources.Remove(_strings);
        resources.Add(replacement);
        _strings = replacement;
    }
}
