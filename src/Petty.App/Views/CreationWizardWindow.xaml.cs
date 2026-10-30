using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Petty.App.Sprites;
using Petty.Core.Animation;
using Petty.Core.Domain;

namespace Petty.App.Views;

/// <summary>Collects the name and appearance; application composition creates the pet.</summary>
public partial class CreationWizardWindow : Window
{
    private SpriteClip? _idle;
    private readonly AnimationPlayback _playback = new();
    private readonly DispatcherTimer _previewTimer = new();

    public CreationWizardWindow(IReadOnlyList<SpriteVariant> variants, bool firstRun, string initialName, string? warning,
        SpriteSheet? initialSheet = null)
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
        _previewTimer.Tick += OnPreviewTick;
        SelectedSheet = initialSheet;
        CoatSelector.ItemsSource = variants;
        CoatSelector.SelectedItem = variants.FirstOrDefault(v => v.AssetId == initialSheet?.Metadata.Character)
            ?? variants.FirstOrDefault(v => v.AssetId == PetCatalog.DefaultAssetId) ?? variants.FirstOrDefault();
        if (CoatSelector.SelectedItem is null)
        {
            ErrorText.Text = "No valid sprite definitions were found. Restore the sprite assets and restart Petty.";
            CreateButton.IsEnabled = false;
        }
        Loaded += (_, _) => NameBox.SelectAll();
        Closed += (_, _) => _previewTimer.Stop();
    }

    public string ConfirmedName { get; private set; } = string.Empty;
    public SpriteVariant? SelectedVariant => CoatSelector.SelectedItem as SpriteVariant;
    public SpriteSheet? SelectedSheet { get; private set; }

    private void OnCoatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedVariant is not { } variant) return;
        try
        {
            if (SelectedSheet?.Metadata.Character != variant.AssetId)
                SelectedSheet = SpriteSheetLoader.Load(variant.Directory, variant.AssetId);
            _idle = SelectedSheet.GetClip(PetAnimationMap.Idle);
            BreedText.Text = variant.BreedId;
            _playback.Play(_idle.Metadata, restart: true);
            _previewTimer.Interval = TimeSpan.FromSeconds(1 / Math.Clamp(_idle.FramesPerSecond, 1, 60));
            _previewTimer.Start();
            ShowPreviewFrame();
            ErrorText.Text = string.Empty;
            CreateButton.IsEnabled = true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or NotSupportedException)
        {
            SelectedSheet = null;
            _idle = null;
            _previewTimer.Stop();
            PreviewImage.Source = null;
            ErrorText.Text = $"Could not load this appearance: {ex.Message}";
            CreateButton.IsEnabled = false;
        }
    }

    private void OnPreviewTick(object? sender, EventArgs e)
    {
        _playback.Advance(_previewTimer.Interval);
        ShowPreviewFrame();
    }

    private void ShowPreviewFrame()
    {
        if (_idle is not null) PreviewImage.Source = _idle[_playback.FrameIndex].Image;
    }

    private void OnGenerateCoat(object sender, RoutedEventArgs e)
    {
        if (_idle is null) return;
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
        if (SelectedSheet is null || SelectedVariant is null) return;
        ConfirmedName = name;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
