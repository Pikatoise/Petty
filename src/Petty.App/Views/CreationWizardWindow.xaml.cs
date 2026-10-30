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
    private sealed record BreedOption(SpriteVariant Variant, string Label);
    private SpriteClip? _idle;
    private readonly AnimationPlayback _playback = new();
    private readonly DispatcherTimer _previewTimer = new();

    public CreationWizardWindow(IReadOnlyList<SpriteVariant> variants, bool firstRun, string initialName, string? warning,
        SpriteSheet? initialSheet = null)
    {
        InitializeComponent();
        if (!firstRun)
        {
            SetResourceReference(TitleProperty, "NewPetTitle");
            HeaderText.SetResourceReference(TextBlock.TextProperty, "NewPetHeading");
            CancelButton.SetResourceReference(ContentControl.ContentProperty, "Cancel");
        }
        NameBox.Text = initialName;
        if (warning is { } text)
        {
            WarningText.Text = text;
            WarningBorder.Visibility = Visibility.Visible;
        }
        _previewTimer.Tick += OnPreviewTick;
        SelectedSheet = initialSheet;
        var representatives = variants.GroupBy(v => v.BreedId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.FirstOrDefault(v => v.AssetId == initialSheet?.Metadata.Character)
                ?? group.FirstOrDefault(v => v.AssetId == PetCatalog.DefaultAssetId)
                ?? group.OrderBy(v => v.AssetId, StringComparer.OrdinalIgnoreCase).First())
            .Select(v => new BreedOption(v, Localization.BreedName(v.BreedId))).ToArray();
        BreedSelector.ItemsSource = representatives;
        BreedSelector.SelectedItem = representatives.FirstOrDefault(v => v.Variant.AssetId == initialSheet?.Metadata.Character)
            ?? representatives.FirstOrDefault(v => v.Variant.AssetId == PetCatalog.DefaultAssetId) ?? representatives.FirstOrDefault();
        if (BreedSelector.SelectedItem is null)
        {
            ErrorText.Text = Localization.Text("NoSprites");
            CreateButton.IsEnabled = false;
        }
        Loaded += (_, _) => NameBox.SelectAll();
        Closed += (_, _) => _previewTimer.Stop();
    }

    public string ConfirmedName { get; private set; } = string.Empty;
    public SpriteVariant? SelectedVariant => (BreedSelector.SelectedItem as BreedOption)?.Variant;
    public SpriteSheet? SelectedSheet { get; private set; }

    private void OnBreedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedVariant is not { } variant) return;
        try
        {
            if (SelectedSheet?.Metadata.Character != variant.AssetId)
                SelectedSheet = SpriteSheetLoader.Load(variant.Directory, variant.AssetId);
            _idle = SelectedSheet.GetClip(PetAnimationMap.Idle);
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
            if (Application.Current is App app) app.LogError("Could not load sprite appearance.", ex);
            ErrorText.Text = Localization.Text("AppearanceLoadError");
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
        if (!PetNameRules.TryNormalize(NameBox.Text, out var name, out _))
        {
            ErrorText.Text = Localization.Text("NameInvalid");
            NameBox.Focus();
            return;
        }
        if (SelectedSheet is null || SelectedVariant is null) return;
        ConfirmedName = name;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
