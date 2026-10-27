using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Petty.Core.Assets;

namespace Petty.App.Sprites;

/// <summary>
/// Locates the shipped atlas both in a build output (<c>Assets/Sprites</c>) and in
/// a developer checkout (<c>assets/sprites</c> relative to the repository root).
/// </summary>
public static class SpriteAssetLocator
{
    public const string MetadataFileName = "cat-orange-white.json";
    public const string ImageFileName = "cat-orange-white.png";

    public static string? FindDirectory()
    {
        foreach (var candidate in Candidates())
        {
            if (IsValid(candidate)) return candidate;
        }

        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "Assets", "Sprites");

        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            yield return Path.Combine(directory.FullName, "assets", "sprites");
            directory = directory.Parent;
        }
    }

    private static bool IsValid(string directory) =>
        File.Exists(Path.Combine(directory, MetadataFileName)) &&
        File.Exists(Path.Combine(directory, ImageFileName));
}

public static class SpriteSheetLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static SpriteSheet Load(string directory)
    {
        var metadataPath = Path.Combine(directory, SpriteAssetLocator.MetadataFileName);
        var metadata = JsonSerializer.Deserialize<SpriteSheetMetadata>(File.ReadAllText(metadataPath), JsonOptions)
                       ?? throw new InvalidDataException($"Sprite metadata '{metadataPath}' is empty.");

        var errors = SpriteMetadataValidator.Validate(metadata);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                "Sprite metadata is invalid:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(e => "  - " + e)));
        }

        var imagePath = Path.Combine(directory, metadata.Image);
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Sprite atlas image '{imagePath}' is missing.", imagePath);

        BitmapSource atlas;
        try
        {
            using var stream = File.OpenRead(imagePath);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
                throw new InvalidDataException("The sprite atlas contains no bitmap frames.");

            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            atlas = converted;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException($"Sprite atlas '{imagePath}' could not be decoded: {ex.Message}", ex);
        }

        var expectedWidth = metadata.Columns * metadata.FrameWidth;
        var expectedHeight = metadata.Rows * metadata.FrameHeight;

        if (atlas.PixelWidth != expectedWidth || atlas.PixelHeight != expectedHeight)
        {
            throw new InvalidDataException(
                $"Sprite atlas is {atlas.PixelWidth}x{atlas.PixelHeight}, " +
                $"but metadata describes a {expectedWidth}x{expectedHeight} grid.");
        }

        var alpha = ExtractAlpha(atlas);

        var clips = new Dictionary<string, SpriteClip>(StringComparer.OrdinalIgnoreCase);
        foreach (var clipMetadata in metadata.Animations)
        {
            var frames = new List<SpriteFrame>(clipMetadata.FrameCount);

            for (var i = 0; i < clipMetadata.FrameCount; i++)
            {
                var cellIndex = clipMetadata.StartIndex + i;
                var (column, row) = SpriteMetadataValidator.CellPosition(metadata, cellIndex);

                var cropped = new CroppedBitmap(
                    atlas,
                    new System.Windows.Int32Rect(
                        column * metadata.FrameWidth,
                        row * metadata.FrameHeight,
                        metadata.FrameWidth,
                        metadata.FrameHeight));

                cropped.Freeze();
                frames.Add(new SpriteFrame(cellIndex, i, cropped) { Column = column, Row = row });
            }

            clips[clipMetadata.Name] = new SpriteClip(clipMetadata, frames);
        }

        return new SpriteSheet(metadata, atlas.PixelWidth, alpha, clips);
    }

    private static byte[] ExtractAlpha(BitmapSource atlas)
    {
        var stride = atlas.PixelWidth * 4;
        var pixels = new byte[stride * atlas.PixelHeight];
        atlas.CopyPixels(pixels, stride, 0);

        var alpha = new byte[atlas.PixelWidth * atlas.PixelHeight];
        for (var i = 0; i < alpha.Length; i++) alpha[i] = pixels[(i * 4) + 3];
        return alpha;
    }
}
