using System.Windows;
using System.Windows.Threading;
using Petty.App.Sprites;
using Petty.Core.Animation;
using Petty.Core.Domain;

namespace Petty.App.Views;

/// <summary>
/// First-run and "new pet" dialog. It only collects a valid name and shows a live
/// preview of the cat; creating and persisting the pet stays in <see cref="App"/>.
/// </summary>
public partial class CreationWizardWindow : Window
{
    private readonly SpriteClip _idle;
    private readonly AnimationPlayback _playback = new();
    private readonly DispatcherTimer _previewTimer;

    public CreationWizardWindow(SpriteSheet sheet, bool firstRun, string initialName, string? warning)
    {
        InitializeComponent();

        if (!firstRun)
        {
            Title = "New pet";
            HeaderText.Text = "Create a new pet";
            CancelButton.Content = "Cancel";
        }

        NameBox.Text = initialName;

        if (warning is { } text)
        {
            WarningText.Text = text;
            WarningBorder.Visibility = Visibility.Visible;
        }

        _idle = sheet.GetClip(PetAnimationMap.Idle);
        _playback.Play(_idle.Metadata);

        _previewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1 / Math.Clamp(_idle.FramesPerSecond, 1, 60)),
        };
        _previewTimer.Tick += OnPreviewTick;
        _previewTimer.Start();

        ShowPreviewFrame();
        Loaded += (_, _) => NameBox.SelectAll();
        Closed += (_, _) => _previewTimer.Stop();
    }

    /// <summary>Validated name; only meaningful when the dialog result is true.</summary>
    public string ConfirmedName { get; private set; } = string.Empty;

    private void OnPreviewTick(object? sender, EventArgs e)
    {
        _playback.Advance(_previewTimer.Interval);
        ShowPreviewFrame();
    }

    private void ShowPreviewFrame() => PreviewImage.Source = _idle[_playback.FrameIndex].Image;

    private void OnGenerateCoat(object sender, RoutedEventArgs e)
    {
        // Only one coat exists, so this replays the preview instead of implying a choice.
        _playback.Play(_idle.Metadata, restart: true);
        ShowPreviewFrame();
        ErrorText.Text = string.Empty;
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (!PetNameRules.TryNormalize(NameBox.Text, out var name, out var error))
        {
            ErrorText.Text = error;
            NameBox.Focus();
            return;
        }

        ConfirmedName = name;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
