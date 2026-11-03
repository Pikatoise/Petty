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
    private sealed record SpeciesOption(string Id, string Label);
    private BreedOption[] _breeds = [];
    private sealed record BreedOption(SpriteVariant Variant, string Label);
    private readonly IReadOnlyList<SpriteVariant> _variants;
    private SpriteVariant[] _coats = [];
    private int _coatIndex;
    private SpriteClip? _idle;
    private readonly AnimationPlayback _playback = new();
    private readonly DispatcherTimer _previewTimer = new();

    public CreationWizardWindow(IReadOnlyList<SpriteVariant> variants, bool firstRun, string initialName, string? warning,
        SpriteSheet? initialSheet = null)
    {
        _variants = variants;
        InitializeComponent();
        AdaptiveWindowLayout.Attach(this);
        SizeChanged += (_, _) => UpdateColumns();
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
        _breeds = variants.GroupBy(v => (v.SpeciesId.ToLowerInvariant(), v.BreedId.ToLowerInvariant()))
            .Select(group => group.FirstOrDefault(v => v.AssetId == initialSheet?.Metadata.Character)
                ?? group.FirstOrDefault(v => v.IsDefault)
                ?? group.FirstOrDefault(v => v.AssetId == PetCatalog.DefaultAssetId)
                ?? group.OrderBy(v => v.AssetId, StringComparer.OrdinalIgnoreCase).First())
            .Select(v => new BreedOption(v, Localization.BreedName(v.BreedId))).ToArray();
        SpeciesOption[] species = _breeds.Select(v => v.Variant.SpeciesId).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new SpeciesOption(id, Localization.SpeciesName(id))).ToArray();
        SpeciesSelector.ItemsSource = species;
        string? initialSpecies = _breeds.FirstOrDefault(v => v.Variant.AssetId == initialSheet?.Metadata.Character)?.Variant.SpeciesId;
        SpeciesSelector.SelectedItem = species.FirstOrDefault(v => v.Id == initialSpecies)
            ?? species.FirstOrDefault(v => v.Id == "cat") ?? species.FirstOrDefault();
        BreedOption? initialBreed = _breeds.FirstOrDefault(v => v.Variant.AssetId == initialSheet?.Metadata.Character);
        if (initialBreed is not null)
        {
            BreedSelector.SelectedItem = initialBreed;
        }

        if (BreedSelector.SelectedItem is null)
        {
            ErrorText.Text = Localization.Text("NoSprites");
            CreateButton.IsEnabled = false;
        }
        Loaded += (_, _) => NameBox.SelectAll();
        Closed += (_, _) => _previewTimer.Stop();
    }

    public string ConfirmedName { get; private set; } = string.Empty;

    private void UpdateColumns()
    {
        bool narrow = Width < 560;
        LayoutGrid.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(192);
        LayoutGrid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(FormPanel, narrow ? 0 : 1);
        Grid.SetRow(FormPanel, narrow ? 1 : 0);
        FormPanel.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(22, 0, 0, 0);
        PreviewBorder.HorizontalAlignment = narrow ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        PreviewBorder.Width = narrow ? Math.Min(192, Math.Max(1, Width - 80)) : double.NaN;
        PreviewImage.Width = narrow ? PreviewBorder.Width : 192;
        PreviewImage.Height = PreviewImage.Width;
    }
    public SpriteVariant? SelectedVariant => _coats.Length == 0 ? null : _coats[_coatIndex];
    public SpriteSheet? SelectedSheet
    {
        get; private set;
    }

    private void OnSpeciesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeciesSelector.SelectedItem is not SpeciesOption species)
        {
            return;
        }

        BreedOption[] options = _breeds.Where(v => v.Variant.SpeciesId.Equals(species.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
        BreedSelector.ItemsSource = options;
        BreedSelector.SelectedItem = options.FirstOrDefault(v => v.Variant.AssetId == PetCatalog.DefaultAssetId) ?? options.FirstOrDefault();
    }

    private void OnBreedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BreedSelector.SelectedItem is not BreedOption breed)
        {
            return;
        }

        _coats = _variants.Where(v => v.SpeciesId.Equals(breed.Variant.SpeciesId, StringComparison.OrdinalIgnoreCase)
                && v.BreedId.Equals(breed.Variant.BreedId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.IsDefault).ThenBy(v => v.AssetId, StringComparer.OrdinalIgnoreCase).ToArray();
        _coatIndex = Math.Max(0, Array.FindIndex(_coats, v => v.AssetId == breed.Variant.AssetId));
        LoadSelectedCoat();
    }

    private void OnPreviousCoat(object sender, RoutedEventArgs e) => ChangeCoat(-1);
    private void OnNextCoat(object sender, RoutedEventArgs e) => ChangeCoat(1);

    private void ChangeCoat(int direction)
    {
        if (_coats.Length < 2)
        {
            return;
        }

        _coatIndex = (_coatIndex + direction + _coats.Length) % _coats.Length;
        LoadSelectedCoat();
    }

    private void LoadSelectedCoat()
    {
        PreviousCoatButton.IsEnabled = NextCoatButton.IsEnabled = _coats.Length > 1;
        CoatPositionText.Text = string.Format(Localization.Text("CoatPositionFormat"), _coatIndex + 1, _coats.Length);
        if (SelectedVariant is not { } variant)
        {
            return;
        }

        try
        {
            if (SelectedSheet?.Metadata.Character != variant.AssetId)
            {
                SelectedSheet = SpriteSheetLoader.Load(variant.Directory, variant.AssetId);
            }

            _idle = SelectedSheet.GetClip(PetAnimationMap.Idle);
            _playback.Play(_idle.Metadata, restart: true);
            _previewTimer.Interval = TimeSpan.FromSeconds(1 / Math.Clamp(_idle.FramesPerSecond, 1, 60));
            _previewTimer.Start();
            ShowPreviewFrame();
            ErrorText.Text = string.Empty;
            CreateButton.IsEnabled = true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or NotSupportedException)
        {
            SelectedSheet = null;
            _idle = null;
            _previewTimer.Stop();
            PreviewImage.Source = null;
            if (Application.Current is App app)
            {
                app.LogError("Could not load sprite appearance.", ex);
            }

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
        if (_idle is not null)
        {
            PreviewImage.Source = _idle[_playback.FrameIndex].Image;
        }
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (!PetNameRules.TryNormalize(NameBox.Text, out string? name, out _))
        {
            ErrorText.Text = Localization.Text("NameInvalid");
            NameBox.Focus();
            return;
        }
        if (SelectedSheet is null || SelectedVariant is null)
        {
            return;
        }

        ConfirmedName = name;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
