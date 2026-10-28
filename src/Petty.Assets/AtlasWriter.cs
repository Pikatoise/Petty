using System.Text.Json;
using Petty.Core.Assets;

namespace Petty.Assets;

public static class AtlasWriter
{
    private const uint SheetBackground = 0xFFF4F4F4;
    private const uint GridColor = 0xFFD2D2D2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static void Write(AtlasBuildResult build, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var atlasPath = Path.Combine(outputDirectory, AtlasBuilder.ImageName);
        build.Atlas.SavePng(atlasPath);

        var metadataPath = Path.Combine(outputDirectory, AtlasBuilder.MetadataName);
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(build.Metadata, JsonOptions));

        var framesDirectory = Path.Combine(outputDirectory, "frames");
        Directory.CreateDirectory(framesDirectory);
        foreach (var frame in build.Frames)
        {
            var fileName = $"{frame.Clip}_{frame.FrameIndex:00}.png";
            frame.Canvas.SavePng(Path.Combine(framesDirectory, fileName));
        }

        var previewDirectory = Path.Combine(outputDirectory, "preview");
        Directory.CreateDirectory(previewDirectory);
        build.Atlas.Upscale(2).SavePng(Path.Combine(previewDirectory, "atlas@2x.png"));
        WriteContactSheet(build, Path.Combine(previewDirectory, "contact-sheet@2x.png"));

        TrayIconBuilder.Write(outputDirectory);
        TrayIconBuilder.WriteIcon(outputDirectory);
        PropSpriteBuilder.Write(outputDirectory);

        Console.WriteLine($"Wrote {AtlasBuilder.ImageName} ({build.Atlas.Width}x{build.Atlas.Height})");
        Console.WriteLine($"Wrote {AtlasBuilder.MetadataName} ({build.Metadata.Animations.Count} clips, {build.Frames.Count} frames)");
        Console.WriteLine($"Wrote {build.Frames.Count} frames to {framesDirectory}");
        Console.WriteLine($"Wrote previews to {previewDirectory}");
        Console.WriteLine($"Wrote {TrayIconBuilder.ImageName} ({TrayIconBuilder.Size}x{TrayIconBuilder.Size})");
        Console.WriteLine($"Wrote {TrayIconBuilder.IconFileName} (32x32 icon)");
        Console.WriteLine($"Wrote bowl and ball sprites ({PropSpriteBuilder.BowlWidth}x{PropSpriteBuilder.BowlHeight}, " +
                          $"{PropSpriteBuilder.BallSize}x{PropSpriteBuilder.BallSize})");
    }

    private static void WriteContactSheet(AtlasBuildResult build, string path)
    {
        const int scale = 2;
        const int cell = CatSpriteRenderer.FrameSize * scale;

        var sheet = new PixelCanvas(AtlasBuilder.Columns * cell, AtlasBuilder.Rows * cell);
        sheet.FillBackground(SheetBackground);

        for (var y = 0; y <= AtlasBuilder.Rows; y++)
            sheet.FillRect(0, Math.Min(y * cell, sheet.Height - 1), sheet.Width, 1, GridColor);

        for (var x = 0; x <= AtlasBuilder.Columns; x++)
            sheet.FillRect(Math.Min(x * cell, sheet.Width - 1), 0, 1, sheet.Height, GridColor);

        var rowByClip = build.Metadata.Animations
            .Select((clip, index) => (clip.Name, index))
            .ToDictionary(pair => pair.Name, pair => pair.index, StringComparer.OrdinalIgnoreCase);

        foreach (var frame in build.Frames)
        {
            var row = rowByClip[frame.Clip];
            var column = frame.FrameIndex;
            var scaled = frame.Canvas.Upscale(scale);
            scaled.DrawOnto(sheet, column * cell, row * cell);
        }

        sheet.SavePng(path);
    }
}
