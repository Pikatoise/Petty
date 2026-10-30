using System.Globalization;
using System.Windows;
using Petty.Core.Preferences;

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

    public void SetLanguage(string? language)
    {
        var updated = (Preferences with { Language = language }).Sanitized();
        _store.Save(updated);
        Preferences = updated;
        Apply();
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
