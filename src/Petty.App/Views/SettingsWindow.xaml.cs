using Petty.Core.Diagnostics;
using Petty.Core.Domain;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Petty.App.Views;

/// <summary>
/// Rename and recreate. Breed and coat are fixed after creation and are only shown.
/// The window never shuts the application down; closing it leaves the pet running.
/// </summary>
public partial class SettingsWindow: Window
{
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0x00, 0x20));
    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x6B, 0x2E));

    private readonly Localization _localization;
    private readonly IAppLog _log;
    private readonly string _breed;
    private readonly string _species;
    private readonly DispatcherTimer _nameSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private string _savedName;
    private bool _ready;

    internal SettingsWindow(string petName, string species, string breed, Localization localization, IAppLog log)
    {
        _localization = localization;
        _log = log;
        _breed = breed;
        _species = species;
        _savedName = petName;
        InitializeComponent();
        NameBox.Text = petName;
        LanguageSelector.SelectedIndex = localization.Preferences.Language switch { "ru" => 1, "en" => 2, _ => 0 };
        AutostartCheck.IsChecked = localization.Preferences.Autostart;
        UpdateAppearance();
        _ready = true;
        _nameSaveTimer.Tick += (_, _) => SaveName();
        Closing += (sender, e) =>
        {
            _nameSaveTimer.Stop();
            if (PetNameRules.TryNormalize(NameBox.Text, out _, out _) && !SaveName()) e.Cancel = true;
        };
        Closed += (_, _) => _nameSaveTimer.Stop();
        Loaded += (_, _) => NameBox.SelectAll();
    }

    public event Func<string, bool>? RenameRequested;

    public event EventHandler? NewPetRequested;

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        _nameSaveTimer.Stop();
        StatusText.Text = string.Empty;
        _nameSaveTimer.Start();
    }

    private void OnNameLostFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (_ready) SaveName();
    }

    private bool SaveName()
    {
        _nameSaveTimer.Stop();
        if (!PetNameRules.TryNormalize(NameBox.Text, out var name, out _))
        {
            ShowStatus(Text("NameInvalid"), ErrorBrush);
            return false;
        }

        if (name == _savedName) return true;
        if (RenameRequested?.Invoke(name) != true)
        {
            ShowSaveError();
            return false;
        }
        _savedName = name;
        ShowStatus(Text("NameSaved"), OkBrush);
        return true;
    }

    private void OnNewPet(object sender, RoutedEventArgs e)
    {
        if (PetNameRules.TryNormalize(NameBox.Text, out _, out _) && !SaveName()) return;
        var confirmation = new ConfirmNewPetWindow { Owner = this };
        if (confirmation.ShowDialog() == true) NewPetRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || LanguageSelector.SelectedItem is not ComboBoxItem item) return;
        var old = _localization.Preferences.Language;
        try
        {
            _localization.SetLanguage(item.Tag as string == "system" ? null : item.Tag as string);
            ProgramStatusText.Text = string.Empty;
            UpdateAppearance();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Error("Could not save preferences.", ex);
            _ready = false;
            LanguageSelector.SelectedIndex = old switch { "ru" => 1, "en" => 2, _ => 0 };
            _ready = true;
            ProgramStatusText.Text = Text("PreferencesError");
        }
    }

    private void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var old = _localization.Preferences.Autostart;
        try
        {
            ProgramStatusText.Text = string.Empty;
            _localization.SetAutostart(AutostartCheck.IsChecked == true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Error("Could not save preferences.", ex);
            _ready = false;
            AutostartCheck.IsChecked = old;
            _ready = true;
            ProgramStatusText.Text = Text("PreferencesError");
        }
    }

    private string Text(string key) => (string)FindResource(key);

    internal void ShowAutostartError() => ProgramStatusText.Text = Text("AutostartError");
    internal void ShowSaveError() => ShowStatus(Text("PetSaveError"), ErrorBrush);

    private void UpdateAppearance() => AppearanceText.Text = string.Format(Text("AppearanceFormat"), Localization.SpeciesName(_species), Localization.BreedName(_breed));

    private void ShowStatus(string text, Brush brush)
    {
        StatusText.Text = text;
        StatusText.Foreground = brush;
    }
}
