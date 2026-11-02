using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Petty.App.Sprites;
using Petty.App.Views;
using Petty.Assets;
using Petty.Core.Assets;
using Petty.Core.Domain;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        string output = Path.Combine(root, "artifacts", "verification", "content");
        Directory.CreateDirectory(output);
        var problems = new List<string>();
        IReadOnlyList<SpriteVariant> variants = SpriteAssetLocator.Discover(reportProblem: problems.Add);
        Require(problems.Count == 0, string.Join("; ", problems));
        Require(variants.Count == 40, $"Expected 40 discovered coats, got {variants.Count}.");
        Require(variants.GroupBy(v => v.BreedId).Count() == 8, "Expected 8 breeds.");
        var silhouettes = new HashSet<string>();
        var atlasHashes = new HashSet<string>();
        int frameCount = 0;
        foreach (SpriteVariant variant in variants)
        {
            SpriteSheet sheet = SpriteSheetLoader.Load(variant.Directory, variant.AssetId);
            Require(sheet.Clips.Count == 9 && sheet.GroundRow == 53, "Invalid runtime clips or ground.");
            Require(SpriteAssetLocator.FindDirectory(variant.AssetId) == variant.Directory, "Nested asset lookup failed.");
            ValidationReport report = AssetValidator.ValidateAtlas(variant.Directory, variant.AssetId);
            Require(report.Passed, string.Join("; ", report.Errors));
            Require(atlasHashes.Add(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(variant.Directory, variant.AssetId + ".png"))))), "Duplicate coat atlas.");
            foreach (SpriteClip clip in sheet.Clips.Values)
            {
                foreach (SpriteFrame frame in clip.Frames)
                {
                    Require(frame.Image.IsFrozen, "Runtime frame must be cached/frozen.");
                    Require(!sheet.IsOpaque(frame.CellIndex, 0, 0), "Transparent corner intercepted input.");
                    byte[] pixels = new byte[64 * 64 * 4];
                    frame.Image.CopyPixels(pixels, 64 * 4, 0);
                    bool hasOpaque = false;
                    for (int y = 0; y < 64; y++)
                    {
                        for (int x = 0; x < 64; x++)
                        {
                            bool opaque = pixels[(y * 64 + x) * 4 + 3] != 0;
                            Require(sheet.IsOpaque(frame.CellIndex, x, y) == opaque, "Hit mask differs from frame alpha.");
                            hasOpaque |= opaque;
                        }
                    }

                    Require(hasOpaque, "Frame has no hit-testable pixels.");
                    frameCount++;
                }
            }

            if (variant.IsDefault)
            {
                byte[] mask = new byte[64 * 64];
                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 64; x++)
                    {
                        mask[y * 64 + x] = sheet.IsOpaque(0, x, y) ? (byte)1 : (byte)0;
                    }
                }

                Require(silhouettes.Add(Convert.ToHexString(SHA256.HashData(mask))), "Breeds share the same silhouette.");
            }
        }
        Require(silhouettes.Count == 8 && frameCount == 2560, "Unexpected silhouette/frame count.");
        Require(SpriteAssetLocator.FindSharedDirectory() is not null, "Shared props missing.");
        PlayPreview.Write(variants, output);

        var app = new Petty.App.App();
        app.InitializeComponent();
        foreach (string? language in new[] { "en", "ru" })
        {
            var strings = new ResourceDictionary { Source = new Uri($"/Petty;component/Resources/Strings.{language}.xaml", UriKind.Relative) };
            app.Resources.MergedDictionaries.Add(strings);
            var wizard = new CreationWizardWindow(variants, true, "Milo", null);
            var species = (ComboBox)wizard.FindName("SpeciesSelector");
            var breeds = (ComboBox)wizard.FindName("BreedSelector");
            Require(species.Items.Count == 2, "Creator must expose two species.");
            for (int s = 0; s < species.Items.Count; s++)
            {
                species.SelectedIndex = s;
                Require(breeds.Items.Count == 4, "Creator must expose four breeds per species.");
                for (int b = 0; b < breeds.Items.Count; b++)
                {
                    breeds.SelectedIndex = b;
                    Require(wizard.SelectedVariant?.IsDefault == true, "Creator selected a non-default coat.");
                    Require(wizard.SelectedSheet?.GetClip("idle").Frames.Count == 8, "Missing live preview frames.");
                    string breedId = wizard.SelectedVariant!.BreedId;
                    Require(app.TryFindResource("Breed-" + breedId) is string, "Missing localized breed name.");
                    Render(wizard, Path.Combine(output, $"creator-{language}-{breedId}.png"));
                    var previous = (Button)wizard.FindName("PreviousCoatButton");
                    var next = (Button)wizard.FindName("NextCoatButton");
                    string initialId = wizard.SelectedVariant.AssetId;
                    Require(previous.IsEnabled && next.IsEnabled, "Coat arrows disabled for a five-coat breed.");
                    previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(wizard.SelectedVariant.AssetId != initialId, "Previous coat did not wrap to the last coat.");
                    next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(wizard.SelectedVariant.AssetId == initialId, "Next coat did not wrap to the first coat.");
                    var chosen = new HashSet<string>();
                    for (int coat = 0; coat < 5; coat++)
                    {
                        Require(wizard.SelectedVariant.BreedId == breedId, "Coat cycling changed breed.");
                        Require(wizard.SelectedSheet?.Metadata.Character == wizard.SelectedVariant.AssetId, "Selected coat and preview disagree.");
                        Require(chosen.Add(wizard.SelectedVariant.AssetId), "Coat repeated before completing the cycle.");
                        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    Require(wizard.SelectedVariant.AssetId == initialId, "Coat cycle did not wrap.");
                }
            }
            wizard.Close();
            SpriteVariant existing = variants.First(v => !v.IsDefault);
            SpriteSheet existingSheet = SpriteSheetLoader.Load(existing.Directory, existing.AssetId);
            var recreation = new CreationWizardWindow(variants, false, "Milo", null, existingSheet);
            Require(recreation.SelectedVariant?.AssetId == existing.AssetId, "Recreation discarded the existing coat.");
            ((Button)recreation.FindName("NextCoatButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(recreation.SelectedVariant?.AssetId != existing.AssetId && recreation.SelectedVariant?.BreedId == existing.BreedId,
                "Recreation coat selection failed.");
            recreation.Close();
            var single = new CreationWizardWindow([variants[0]], true, "Milo", null);
            Require(!((Button)single.FindName("PreviousCoatButton")).IsEnabled && !((Button)single.FindName("NextCoatButton")).IsEnabled,
                "Single-coat breed should disable arrows.");
            single.Close();
            app.Resources.MergedDictionaries.Remove(strings);
        }

        // Rendering the configured art again must reproduce the installed PNG bytes.
        foreach (BreedDesign breed in BreedDesign.Load(Path.Combine(root, "assets", "breed-designs.json")))
        {
            foreach (CoatDesign coat in breed.Coats)
            {
                string path = Path.Combine(root, "assets", "sprites", "packs", breed.Id, breed.Id + "-" + coat.Id + ".png");
                PixelCanvas built = BreedPackBuilder.Build(breed, coat).Atlas;
                Require(PixelCanvas.LoadPng(path).Pixels.SequenceEqual(built.Pixels), $"Generation is not reproducible: {breed.Id}/{coat.Id}.");
                Require(File.ReadAllBytes(path).AsSpan().SequenceEqual(built.ToPngBytes()), $"PNG encoding depends on host context: {breed.Id}/{coat.Id}.");
            }
        }

        Console.WriteLine($"CONTENT VERIFIED: 40 runtime atlases, {frameCount} frozen/hit-tested frames, 8 unique silhouettes, 16 localized creator previews, coat cycling/wrapping, existing-coat recreation, deterministic PNGs.");
    }

    private static void Render(Window window, string path)
    {
        if (!window.IsVisible)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000;
            window.Top = -10000;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Show();
        }
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * 2), (int)Math.Ceiling(window.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
