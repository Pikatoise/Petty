using System.Windows;
using System.Windows.Media;
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

    public SettingsWindow(string petName)
    {
        InitializeComponent();
        NameBox.Text = petName;
        Loaded += (_, _) => NameBox.SelectAll();
    }

    public event EventHandler<string>? RenameRequested;

    public event EventHandler? NewPetRequested;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!PetNameRules.TryNormalize(NameBox.Text, out var name, out var error))
        {
            ShowStatus(error!, ErrorBrush);
            NameBox.Focus();
            return;
        }

        RenameRequested?.Invoke(this, name);
        ShowStatus("Name saved.", OkBrush);
    }

    private void OnNewPet(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "Create a new pet?" + Environment.NewLine + Environment.NewLine +
            "This resets the current pet's name and needs. Its saved state is replaced.",
            "Petty",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes) NewPetRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void ShowStatus(string text, Brush brush)
    {
        StatusText.Text = text;
        StatusText.Foreground = brush;
    }
}
