using System.Text.Json;
using Petty.Core.Assets;

namespace Petty.Assets;

public sealed record ValidationReport(bool Passed, IReadOnlyList<string> Errors, IReadOnlyList<string> Notes);

/// <summary>
/// Re-decodes the written PNG (not the in-memory canvas) and checks the hard
/// requirements from the brief: real alpha, no anti-aliasing, limited palette,
/// complete frames, consistent baseline and a seamless walk loop.
/// </summary>
public static class AssetValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> StandingClips = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle", "walk", "eat", "tired", "exit",
    };

    public static ValidationReport ValidateDirectory(string directory, Variant variant) =>
        ValidateAtlas(directory, variant.Id, variant.Source is null ? variant.Colors : null, checkShared: true);

    public static ValidationReport ValidateAtlas(string directory, string id, uint[]? palette = null, bool checkShared = false)
    {
        var errors = new List<string>();
        var notes = new List<string>();

        if (!SpriteMetadataValidator.IsValidId(id)) return new ValidationReport(false, ["Invalid asset ID."], notes);
        var metadataPath = Path.Combine(directory, id + ".json");
        if (!File.Exists(metadataPath))
        {
            errors.Add($"Missing metadata file: {metadataPath}");
            return new ValidationReport(false, errors, notes);
        }

        SpriteSheetMetadata? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<SpriteSheetMetadata>(File.ReadAllText(metadataPath), JsonOptions);
        }
        catch (JsonException ex)
        {
            errors.Add($"Metadata is not valid JSON: {ex.Message}");
            return new ValidationReport(false, errors, notes);
        }

        if (metadata is null)
        {
            errors.Add("Metadata deserialised to null.");
            return new ValidationReport(false, errors, notes);
        }

        errors.AddRange(SpriteMetadataValidator.Validate(metadata, requirePet: true));
        if (metadata.Character != id || metadata.Image != id + ".png")
            errors.Add("Metadata character or image does not match the variant ID.");
        if (errors.Count > 0) return new ValidationReport(false, errors, notes);

        var imagePath = Path.Combine(directory, metadata.Image);
        if (!File.Exists(imagePath))
        {
            errors.Add($"Missing atlas image: {imagePath}");
            return new ValidationReport(false, errors, notes);
        }

        PixelCanvas atlas;
        try
        {
            using var stream = File.OpenRead(imagePath);
            if (!SpritePixels.HasRgbaPngHeader(stream))
                return new ValidationReport(false, ["Atlas must be an 8-bit RGBA PNG."], notes);
            atlas = PixelCanvas.LoadPng(imagePath);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
        {
            errors.Add($"Atlas image cannot be decoded: {ex.Message}");
            return new ValidationReport(false, errors, notes);
        }

        var expectedWidth = metadata.Columns * metadata.FrameWidth;
        var expectedHeight = metadata.Rows * metadata.FrameHeight;

        if (atlas.Width != expectedWidth || atlas.Height != expectedHeight)
        {
            errors.Add($"Atlas is {atlas.Width}x{atlas.Height}, expected {expectedWidth}x{expectedHeight}.");
            return new ValidationReport(false, errors, notes);
        }

        var semiTransparent = 0;
        var unknownColors = new HashSet<uint>();
        var colors = palette?.ToHashSet();

        for (var y = 0; y < atlas.Height; y++)
        {
            for (var x = 0; x < atlas.Width; x++)
            {
                var color = atlas[x, y];
                var alpha = (color >> 24) & 0xFF;

                if (alpha == 0) continue;
                if (alpha != 255) semiTransparent++;
                if (colors is not null && !colors.Contains(color)) unknownColors.Add(color);
            }
        }

        if (semiTransparent > 0)
            errors.Add($"{semiTransparent} pixels are not fully opaque or fully transparent (anti-aliasing).");

        if (unknownColors.Count > 0)
            errors.Add($"{unknownColors.Count} colours are outside the palette: {string.Join(", ", unknownColors.Take(5).Select(c => $"#{c & 0xFFFFFF:X6}"))}");

        var occupiedCells = new HashSet<int>();
        var maxRowByClip = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var clip in metadata.Animations)
        {
            for (var i = 0; i < clip.FrameCount; i++)
            {
                var cellIndex = clip.StartIndex + i;
                occupiedCells.Add(cellIndex);

                var (column, row) = SpriteMetadataValidator.CellPosition(metadata, cellIndex);
                var frame = CropFrame(atlas, metadata, column, row);
                var bounds = Measure(frame);

                if (bounds.OpaqueCount == 0)
                {
                    errors.Add($"Clip '{clip.Name}' frame {i} is empty.");
                    continue;
                }

                if (bounds.MinX < 1 || bounds.MinY < 1 ||
                    bounds.MaxX >= metadata.FrameWidth - 1 ||
                    bounds.MaxY >= metadata.FrameHeight - 1)
                {
                    errors.Add($"Clip '{clip.Name}' frame {i} touches the cell edge ({bounds}).");
                }

                maxRowByClip[clip.Name] = maxRowByClip.TryGetValue(clip.Name, out var previous)
                    ? Math.Max(previous, bounds.MaxY)
                    : bounds.MaxY;
            }
        }

        foreach (var clip in metadata.Animations)
        {
            if (!maxRowByClip.TryGetValue(clip.Name, out var maxRow)) continue;

            if (StandingClips.Contains(clip.Name) && maxRow != metadata.GroundRow)
                errors.Add($"Clip '{clip.Name}' baseline is row {maxRow}, expected {metadata.GroundRow}.");
        }

        foreach (var cell in Enumerable.Range(0, metadata.CellCount).Except(occupiedCells))
            if (Measure(CropFrame(atlas, metadata, cell)).OpaqueCount > 0)
                errors.Add($"Unused atlas cell {cell} is not transparent.");

        CheckWalkLoop(atlas, metadata, errors, notes);
        if (checkShared)
        {
        CheckSmallSprite(directory, errors, notes,
            TrayIconBuilder.ImageName, TrayIconBuilder.Size, TrayIconBuilder.Size, "tray icon", keepMargin: true);
        CheckSmallSprite(directory, errors, notes,
            PropSpriteBuilder.BowlEmptyName, PropSpriteBuilder.BowlWidth, PropSpriteBuilder.BowlHeight, "empty bowl", keepMargin: false);
        CheckSmallSprite(directory, errors, notes,
            PropSpriteBuilder.BowlFullName, PropSpriteBuilder.BowlWidth, PropSpriteBuilder.BowlHeight, "full bowl", keepMargin: false);
        CheckSmallSprite(directory, errors, notes,
            PropSpriteBuilder.BallName, PropSpriteBuilder.BallSize, PropSpriteBuilder.BallSize, "ball", keepMargin: true);

        CheckBowlMasks(directory, errors);
        }

        notes.Add($"atlas {atlas.Width}x{atlas.Height}, {metadata.Animations.Count} clips, {occupiedCells.Count} frames");
        notes.Add($"palette colours used: {CountPaletteColors(atlas)}");

        return new ValidationReport(errors.Count == 0, errors, notes);
    }

    /// <summary>
    /// Small hand-drawn props share the atlas rules (hard alpha, palette, a real
    /// silhouette). The click-through masks of the two bowls must match, because the
    /// bowl window hit-tests one mask while showing either picture.
    /// </summary>
    private static void CheckSmallSprite(
        string directory,
        List<string> errors,
        List<string> notes,
        string fileName,
        int width,
        int height,
        string label,
        bool keepMargin)
    {
        var path = Path.Combine(directory, fileName);

        if (!File.Exists(path))
        {
            errors.Add($"Missing {label}: {path}");
            return;
        }

        var sprite = PixelCanvas.LoadPng(path);

        if (sprite.Width != width || sprite.Height != height)
        {
            errors.Add($"The {label} is {sprite.Width}x{sprite.Height}, expected {width}x{height}.");
            return;
        }

        var bounds = Measure(sprite);
        if (bounds.OpaqueCount == 0) errors.Add($"The {label} is empty.");

        if (keepMargin && (bounds.MinX < 1 || bounds.MinY < 1 ||
                           bounds.MaxX >= sprite.Width - 1 || bounds.MaxY >= sprite.Height - 1))
        {
            errors.Add($"The {label} touches its edge ({bounds}).");
        }

        for (var y = 0; y < sprite.Height; y++)
        {
            for (var x = 0; x < sprite.Width; x++)
            {
                var color = sprite[x, y];
                var alpha = (color >> 24) & 0xFF;

                if (alpha != 0 && alpha != 255) errors.Add($"The {label} has semi-transparent pixels.");
                if (alpha == 255 && !Palette.Set.Contains(color))
                    errors.Add($"The {label} uses a colour outside the palette: #{color & 0xFFFFFF:X6}.");
            }
        }

        notes.Add($"{label} {sprite.Width}x{sprite.Height}, {bounds.OpaqueCount} opaque pixels");
    }

    private static void CheckBowlMasks(string directory, List<string> errors)
    {
        var empty = Path.Combine(directory, PropSpriteBuilder.BowlEmptyName);
        var full = Path.Combine(directory, PropSpriteBuilder.BowlFullName);
        if (!File.Exists(empty) || !File.Exists(full)) return;

        var a = PixelCanvas.LoadPng(empty);
        var b = PixelCanvas.LoadPng(full);
        if (a.Width != b.Width || a.Height != b.Height) return;

        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                if (a.IsOpaque(x, y) == b.IsOpaque(x, y)) continue;

                errors.Add($"The empty and full bowl differ in opacity at {x},{y}; the window hit-tests one mask.");
                return;
            }
        }
    }

    private static void CheckWalkLoop(
        PixelCanvas atlas,
        SpriteSheetMetadata metadata,
        List<string> errors,
        List<string> notes)
    {
        var walk = metadata.Animations.FirstOrDefault(a => a.Name.Equals("walk", StringComparison.OrdinalIgnoreCase));
        if (walk is null || walk.FrameCount < 2) return;

        var first = CropFrame(atlas, metadata, walk.StartIndex);
        var second = CropFrame(atlas, metadata, walk.StartIndex + 1);
        var last = CropFrame(atlas, metadata, walk.StartIndex + walk.FrameCount - 1);

        var loopDifference = Difference(first, last);
        var stepDifference = Math.Max(1, Difference(first, second));
        var ratio = loopDifference / (double)Math.Max(1, stepDifference);

        notes.Add($"walk loop step ratio: {ratio:F2} (loop diff {loopDifference}, normal step {stepDifference})");

        if (ratio > 2.5)
            errors.Add($"Walk cycle jumps between the last and first frame ({ratio:F2}x a normal step).");
    }

    private static int Difference(PixelCanvas a, PixelCanvas b)
    {
        var different = 0;

        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                if (a[x, y] != b[x, y]) different++;
            }
        }

        return different;
    }

    private static PixelCanvas CropFrame(PixelCanvas atlas, SpriteSheetMetadata metadata, int cellIndex)
    {
        var (column, row) = SpriteMetadataValidator.CellPosition(metadata, cellIndex);
        return CropFrame(atlas, metadata, column, row);
    }

    private static PixelCanvas CropFrame(PixelCanvas atlas, SpriteSheetMetadata metadata, int column, int row)
    {
        var frame = new PixelCanvas(metadata.FrameWidth, metadata.FrameHeight);

        for (var y = 0; y < metadata.FrameHeight; y++)
        {
            for (var x = 0; x < metadata.FrameWidth; x++)
            {
                frame[x, y] = atlas[(column * metadata.FrameWidth) + x, (row * metadata.FrameHeight) + y];
            }
        }

        return frame;
    }

    private readonly record struct FrameBounds(int MinX, int MinY, int MaxX, int MaxY, int OpaqueCount);

    private static FrameBounds Measure(PixelCanvas frame)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = -1;
        var maxY = -1;
        var count = 0;

        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (!frame.IsOpaque(x, y)) continue;

                count++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return count == 0 ? new FrameBounds(0, 0, 0, 0, 0) : new FrameBounds(minX, minY, maxX, maxY, count);
    }

    private static int CountPaletteColors(PixelCanvas canvas)
    {
        var colors = new HashSet<uint>();

        for (var y = 0; y < canvas.Height; y++)
            for (var x = 0; x < canvas.Width; x++)
                if (canvas.IsOpaque(x, y)) colors.Add(canvas[x, y]);

        return colors.Count;
    }
}
