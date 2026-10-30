using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using Petty.Core.Diagnostics;
using Petty.Core.Domain;

namespace Petty.App.Views;

/// <summary>
/// Rename and recreate. Breed and coat are fixed after creation and are only shown.
/// The window never shuts the application down; closing it leaves the pet running.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0x00, 0x20));
    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x6B, 0x2E));

    private readonly Localization _localization;
    private readonly IAppLog _log;
    private readonly string _breed;
    private readonly string _appearance;
    private bool _ready;

    internal SettingsWindow(string petName, string breed, string appearance, Localization localization, IAppLog log)
    {
        _localization = localization;
        _log = log;
        _breed = breed;
        _appearance = appearance;
        InitializeComponent();
        NameBox.Text = petName;
        LanguageSelector.SelectedIndex = localization.Preferences.Language switch { "ru" => 1, "en" => 2, _ => 0 };
        UpdateAppearance();
        _ready = true;
        Loaded += (_, _) => NameBox.SelectAll();
    }

    public event EventHandler<string>? RenameRequested;

    public event EventHandler? NewPetRequested;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!PetNameRules.TryNormalize(NameBox.Text, out var name, out _))
        {
            ShowStatus(Text("NameInvalid"), ErrorBrush);
            NameBox.Focus();
            return;
        }

        RenameRequested?.Invoke(this, name);
        ShowStatus(Text("NameSaved"), OkBrush);
    }

    private void OnNewPet(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            Text("ConfirmNew"),
            "Petty",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes) NewPetRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || LanguageSelector.SelectedItem is not ComboBoxItem item) return;
        var old = _localization.Preferences.Language;
        try
        {
            _localization.SetLanguage(item.Tag as string == "system" ? null : item.Tag as string);
            StatusText.Text = string.Empty;
            UpdateAppearance();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Error("Could not save preferences.", ex);
            _ready = false;
            LanguageSelector.SelectedIndex = old switch { "ru" => 1, "en" => 2, _ => 0 };
            _ready = true;
            ShowStatus(Text("PreferencesError"), ErrorBrush);
        }
    }

    private string Text(string key) => (string)FindResource(key);

    private void UpdateAppearance() => AppearanceText.Text = string.Format(Text("AppearanceFormat"), _breed, _appearance);

    private void ShowStatus(string text, Brush brush)
    {
        StatusText.Text = text;
        StatusText.Foreground = brush;
    }
}
