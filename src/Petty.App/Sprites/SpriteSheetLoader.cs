using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Petty.Core.Assets;
using Petty.Core.Domain;

namespace Petty.App.Sprites;

/// <summary>
/// Locates the shipped atlas both in a build output (<c>Assets/Sprites</c>) and in
/// a developer checkout (<c>assets/sprites</c> relative to the repository root).
/// </summary>
public sealed record SpriteVariant(string AssetId, string BreedId, string CoatId, string DisplayName, string Directory)
{
    public override string ToString() => DisplayName;
}

public static class SpriteAssetLocator
{
    /// <summary>Discover metadata in the optional user directory and bundled sprite directory.
    /// User assets take precedence when IDs match; atlas images are decoded only on selection.</summary>
    public static IReadOnlyList<SpriteVariant> Discover(string? customDirectory = null, Action<string>? reportProblem = null)
    {
        var variants = new Dictionary<string, SpriteVariant>(StringComparer.OrdinalIgnoreCase);
        var directories = customDirectory is null ? Candidates() : new[] { customDirectory }.Concat(Candidates());
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                if (id.Equals("catalog", StringComparison.OrdinalIgnoreCase) || !IsValidId(id) || variants.ContainsKey(id)) continue;
                try
                {
                    var metadata = JsonSerializer.Deserialize<SpriteSheetMetadata>(File.ReadAllText(path),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (metadata is null || metadata.Character != id ||
                        SpriteMetadataValidator.Validate(metadata).Count != 0 ||
                        Path.GetFileName(metadata.Image) != metadata.Image ||
                        !File.Exists(Path.Combine(directory, metadata.Image)))
                    {
                        reportProblem?.Invoke($"Invalid sprite definition: {path}");
                        continue;
                    }
                    var breed = metadata.BreedId ?? PetCatalog.DefaultBreedId;
                    var coat = metadata.CoatId ?? (id == PetCatalog.DefaultAssetId ? PetCatalog.DefaultCoatId : id);
                    variants.Add(id, new SpriteVariant(id, breed, coat, string.IsNullOrWhiteSpace(metadata.DisplayName) ? id.Replace('-', ' ') : metadata.DisplayName, directory));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    reportProblem?.Invoke($"Could not read sprite definition {path}: {ex.Message}");
                }
            }
        }
        return variants.Values.OrderBy(v => v.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static bool IsValidId(string id) =>
        !string.IsNullOrEmpty(id) && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    public static string? FindDirectory(string assetId)
    {
        if (string.IsNullOrEmpty(assetId) || assetId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))
            throw new InvalidDataException("Invalid sprite asset ID.");
        foreach (var candidate in Candidates())
        {
            if (File.Exists(Path.Combine(candidate, assetId + ".json"))) return candidate;
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

}

public static class SpriteSheetLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static SpriteSheet Load(string directory, string assetId)
    {
        if (string.IsNullOrEmpty(assetId) || assetId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))
            throw new InvalidDataException("Invalid sprite asset ID.");
        var metadataPath = Path.Combine(directory, assetId + ".json");
        var metadata = JsonSerializer.Deserialize<SpriteSheetMetadata>(File.ReadAllText(metadataPath), JsonOptions)
                       ?? throw new InvalidDataException($"Sprite metadata '{metadataPath}' is empty.");

        var errors = SpriteMetadataValidator.Validate(metadata);
        if (metadata.Character != assetId || Path.GetFileName(metadata.Image) != metadata.Image)
            throw new InvalidDataException("Sprite metadata has an invalid character or image path.");
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
