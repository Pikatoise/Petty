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
public sealed record SpriteVariant(string AssetId, string BreedId, string CoatId, string DisplayName, string Directory, string SpeciesId = "cat", bool IsDefault = false)
{
    public override string ToString() => DisplayName;
}

public static class SpriteAssetLocator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Discover metadata in the optional user directory and bundled sprite directory.
    /// User assets take precedence when IDs match; atlas images are decoded only on selection.</summary>
    public static IReadOnlyList<SpriteVariant> Discover(string? customDirectory = null, Action<string>? reportProblem = null)
    {
        var variants = new Dictionary<string, SpriteVariant>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> directories = customDirectory is null ? Candidates() : new[] { customDirectory }.Concat(Candidates());
        foreach (string? directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            string packRoot = Path.Combine(directory, "packs");
            IEnumerable<string> packDirectories = Directory.Exists(packRoot)
                ? Directory.EnumerateDirectories(packRoot).Where(p => SpriteMetadataValidator.IsValidId(Path.GetFileName(p)))
                : Enumerable.Empty<string>();
            string[] paths = new[] { directory }.Concat(packDirectories)
                .SelectMany(d => Directory.EnumerateFiles(d, "*.json"))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            var duplicates = paths.GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string? id in duplicates)
            {
                reportProblem?.Invoke($"Duplicate sprite ID '{id}' in {directory}.");
            }

            foreach (string? path in paths)
            {
                string id = Path.GetFileNameWithoutExtension(path);
                if (id.Equals("catalog", StringComparison.OrdinalIgnoreCase) || id == PetCatalog.LegacyAssetId || !SpriteMetadataValidator.IsValidId(id) || duplicates.Contains(id) || variants.ContainsKey(id))
                {
                    continue;
                }

                try
                {
                    SpriteSheetMetadata? metadata = JsonSerializer.Deserialize<SpriteSheetMetadata>(File.ReadAllText(path), JsonOptions);
                    if (metadata is null || metadata.Character != id ||
                        SpriteMetadataValidator.Validate(metadata).Count != 0 ||
                        Path.GetFileName(metadata.Image) != metadata.Image ||
                        !File.Exists(Path.Combine(Path.GetDirectoryName(path)!, metadata.Image)))
                    {
                        reportProblem?.Invoke($"Invalid sprite definition: {path}");
                        continue;
                    }
                    string breed = metadata.BreedId ?? "cat";
                    string coat = metadata.CoatId ?? (id == PetCatalog.DefaultAssetId ? PetCatalog.DefaultCoatId : id);
                    variants.Add(id, new SpriteVariant(id, breed, coat, string.IsNullOrWhiteSpace(metadata.DisplayName) ? id.Replace('-', ' ') : metadata.DisplayName, Path.GetDirectoryName(path)!, metadata.SpeciesId ?? "cat", metadata.IsDefault));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    reportProblem?.Invoke($"Could not read sprite definition {path}: {ex.Message}");
                }
            }
        }
        return variants.Values.OrderBy(v => v.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static string? FindDirectory(string assetId)
    {
        if (!SpriteMetadataValidator.IsValidId(assetId))
        {
            throw new InvalidDataException("Invalid sprite asset ID.");
        }

        return Discover().FirstOrDefault(v => v.AssetId == assetId)?.Directory;
    }

    public static string? FindSharedDirectory() => Candidates().FirstOrDefault(directory =>
        File.Exists(Path.Combine(directory, "tray-cat.png")) && File.Exists(Path.Combine(directory, "bowl-empty.png")));

    private static IEnumerable<string> Candidates()
    {
        string baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "Assets", "Sprites");

        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            yield return Path.Combine(directory.FullName, "assets", "sprites");
            directory = directory.Parent;
        }
        yield return EmbeddedContent.Sprites;
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
        if (!SpriteMetadataValidator.IsValidId(assetId))
        {
            throw new InvalidDataException("Invalid sprite asset ID.");
        }

        string metadataPath = Path.Combine(directory, assetId + ".json");
        SpriteSheetMetadata metadata = JsonSerializer.Deserialize<SpriteSheetMetadata>(File.ReadAllText(metadataPath), JsonOptions)
                       ?? throw new InvalidDataException($"Sprite metadata '{metadataPath}' is empty.");

        IReadOnlyList<string> errors = SpriteMetadataValidator.Validate(metadata);
        if (metadata.Character != assetId || Path.GetFileName(metadata.Image) != metadata.Image)
        {
            throw new InvalidDataException("Sprite metadata has an invalid character or image path.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                "Sprite metadata is invalid:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(e => "  - " + e)));
        }

        string imagePath = Path.Combine(directory, metadata.Image);
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException($"Sprite atlas image '{imagePath}' is missing.", imagePath);
        }

        BitmapSource atlas;
        try
        {
            using FileStream stream = File.OpenRead(imagePath);
            if (!SpritePixels.HasRgbaPngHeader(stream))
            {
                throw new InvalidDataException("Sprite atlas must be an 8-bit RGBA PNG.");
            }

            stream.Position = 0;
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                throw new InvalidDataException("The sprite atlas contains no bitmap frames.");
            }

            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            atlas = converted;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException($"Sprite atlas '{imagePath}' could not be decoded: {ex.Message}", ex);
        }

        int expectedWidth = metadata.Columns * metadata.FrameWidth;
        int expectedHeight = metadata.Rows * metadata.FrameHeight;

        if (atlas.PixelWidth != expectedWidth || atlas.PixelHeight != expectedHeight)
        {
            throw new InvalidDataException(
                $"Sprite atlas is {atlas.PixelWidth}x{atlas.PixelHeight}, " +
                $"but metadata describes a {expectedWidth}x{expectedHeight} grid.");
        }

        byte[] pixelBytes = new byte[atlas.PixelWidth * atlas.PixelHeight * 4];
        atlas.CopyPixels(pixelBytes, atlas.PixelWidth * 4, 0);
        IReadOnlyList<string> pixelErrors = SpritePixels.Validate(metadata, pixelBytes);
        if (pixelErrors.Count > 0)
        {
            throw new InvalidDataException("Sprite atlas is invalid: " + string.Join("; ", pixelErrors));
        }

        byte[] alpha = ExtractAlpha(pixelBytes);
        var clips = new Dictionary<string, SpriteClip>(StringComparer.OrdinalIgnoreCase);
        foreach (AnimationClipMetadata clipMetadata in metadata.Animations)
        {
            var frames = new List<SpriteFrame>(clipMetadata.FrameCount);

            for (int i = 0; i < clipMetadata.FrameCount; i++)
            {
                int cellIndex = clipMetadata.StartIndex + i;
                (int column, int row) = SpriteMetadataValidator.CellPosition(metadata, cellIndex);

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

    private static byte[] ExtractAlpha(byte[] pixels)
    {
        byte[] alpha = new byte[pixels.Length / 4];
        for (int i = 0; i < alpha.Length; i++)
        {
            alpha[i] = pixels[(i * 4) + 3];
        }

        return alpha;
    }
}
