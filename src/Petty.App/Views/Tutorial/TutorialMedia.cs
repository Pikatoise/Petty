using System.IO;
using System.Windows.Controls;
using Petty.App.Sprites;

namespace Petty.App.Views.Tutorial;

/// <summary>Decoded artwork and sprite placement shared by the isolated tutorial scenes.</summary>
internal sealed class TutorialMedia
{
    internal const double Floor = 183;
    internal const double Scale = 2;
    private readonly Dictionary<string, SpriteSheet> _sheets = new();
    private readonly Dictionary<string, PropImage> _props = new();

    public TutorialMedia(IReadOnlyList<SpriteVariant> variants, string assetDirectory)
    {
        foreach (string species in new[] { "cat", "dog" })
        {
            SpriteVariant variant = variants.FirstOrDefault(v => v.SpeciesId == species && v.IsDefault)
                ?? variants.FirstOrDefault(v => v.SpeciesId == species) ?? variants.First();
            _sheets[species] = SpriteSheetLoader.Load(variant.Directory, variant.AssetId);
        }
        foreach (string name in new[] { "bowl-empty", "bowl-25", "bowl-50", "bowl-75", "bowl-full", "ball", "hand", "tray-cat" }
            .Concat(Enumerable.Range(0, 6).Select(i => $"pour-{i}")))
        {
            _props[name] = PropImage.Load(Path.Combine(assetDirectory, name + ".png"));
        }
    }

    public SpriteSheet Sheet(string species) => _sheets[species];
    public PropImage Prop(string name) => _props[name];

    public static void ShowPet(Image image, SpriteSheet sheet, string animation, double elapsed, double left, int? frame = null)
    {
        SpriteClip clip = sheet.GetClip(animation);
        image.Source = clip[frame ?? (int)(elapsed * clip.FramesPerSecond) % clip.Frames.Count].Image;
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, Floor - sheet.GroundRow * Scale);
    }

    public void ShowProp(Image image, string name, double left, double floor)
    {
        PropImage prop = _props[name];
        image.Source = prop.Image;
        image.Width = prop.Width * Scale;
        image.Height = prop.Height * Scale;
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, floor - prop.LastOpaqueRow * Scale);
    }
}
