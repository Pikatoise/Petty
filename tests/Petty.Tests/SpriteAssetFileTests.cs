using System.Buffers.Binary;
using Petty.Core.Assets;
using Petty.Core.Domain;

namespace Petty.Tests;

/// <summary>
/// Checks the asset files that actually ship in the repository, independent of the
/// generator: metadata consistency, PNG dimensions and a real RGBA alpha channel.
/// </summary>
public class SpriteAssetFileTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string SpriteDirectory = Path.Combine(RepositoryRoot, "assets", "sprites");
    private static readonly string MetadataPath = Path.Combine(SpriteDirectory, "packs", PetCatalog.DefaultBreedId, PetCatalog.DefaultAssetId + ".json");

    [Fact]
    public void ShippedMetadataIsValidAndCoversEveryFrame()
    {
        Assert.True(File.Exists(MetadataPath), $"Missing metadata: {MetadataPath}");

        SpriteSheetMetadata? metadata = System.Text.Json.JsonSerializer.Deserialize<SpriteSheetMetadata>(
            File.ReadAllText(MetadataPath),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.NotNull(metadata);
        Assert.Empty(SpriteMetadataValidator.Validate(metadata, requirePet: true));

        int totalFrames = metadata!.Animations.Sum(a => a.FrameCount);
        Assert.Equal(64, totalFrames);

        var expected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["idle"] = 8,
            ["walk"] = 8,
            ["run"] = 8,
            ["eat"] = 6,
            ["sleep"] = 4,
            ["play"] = 12,
            ["pet"] = 6,
            ["tired"] = 4,
            ["exit"] = 8,
        };

        foreach ((string? name, int count) in expected)
        {
            AnimationClipMetadata clip = Assert.Single(metadata.Animations, a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(count, clip.FrameCount);
            Assert.True(clip.FramesPerSecond > 0);
        }

        Assert.False(metadata.Animations.Single(a => a.Name == "exit").Loop);
    }

    [Fact]
    public void ShippedPacksContainEightBreedsAndFortyValidCoats()
    {
        string[] paths = Directory.GetFiles(Path.Combine(SpriteDirectory, "packs"), "*.json", SearchOption.AllDirectories);
        Assert.Equal(40, paths.Length);
        var metadataList = new List<SpriteSheetMetadata>();
        foreach (string path in paths)
        {
            string id = Path.GetFileNameWithoutExtension(path);
            SpriteSheetMetadata metadata = System.Text.Json.JsonSerializer.Deserialize<SpriteSheetMetadata>(
                File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            Assert.Equal(id, metadata.Character);
            Assert.Empty(SpriteMetadataValidator.Validate(metadata, requirePet: true));
            PngHeader header = ReadPngHeader(Path.Combine(Path.GetDirectoryName(path)!, metadata.Image));
            Assert.Equal(metadata.Columns * metadata.FrameWidth, header.Width);
            Assert.Equal(metadata.Rows * metadata.FrameHeight, header.Height);
            Assert.Equal(8, header.BitDepth);
            Assert.Equal(6, header.ColorType);
            metadataList.Add(metadata);
        }
        Assert.Equal(40, metadataList.Select(m => m.Character).Distinct().Count());
        Assert.Equal(8, metadataList.Select(m => m.BreedId).Distinct().Count());
        Assert.Equal(4, metadataList.Where(m => m.SpeciesId == "cat").Select(m => m.BreedId).Distinct().Count());
        Assert.Equal(4, metadataList.Where(m => m.SpeciesId == "dog").Select(m => m.BreedId).Distinct().Count());
        foreach (IGrouping<string?, SpriteSheetMetadata> breed in metadataList.GroupBy(m => m.BreedId))
        {
            Assert.Equal(5, breed.Count());
            Assert.Equal(5, breed.Select(m => m.CoatId).Distinct().Count());
            Assert.Single(breed, m => m.IsDefault);
        }
        Assert.DoesNotContain(metadataList, m => m.Character == PetCatalog.LegacyAssetId);
        Assert.False(File.Exists(Path.Combine(SpriteDirectory, PetCatalog.LegacyAssetId + ".png")));
        Assert.False(File.Exists(Path.Combine(SpriteDirectory, PetCatalog.LegacyAssetId + ".json")));
    }

    [Fact]
    public void TrayIconIsACrisp32pxPng()
    {
        string path = Path.Combine(SpriteDirectory, "tray-cat.png");
        Assert.True(File.Exists(path), $"Missing tray icon: {path}");

        PngHeader header = ReadPngHeader(path);

        Assert.Equal(32, header.Width);
        Assert.Equal(32, header.Height);
        Assert.Equal(8, header.BitDepth);
        Assert.Equal(6, header.ColorType);
    }

    [Fact]
    public void WindowIconIsAWellFormed32pxIco()
    {
        string path = Path.Combine(SpriteDirectory, "petty.ico");
        Assert.True(File.Exists(path), $"Missing window icon: {path}");

        byte[] bytes = File.ReadAllBytes(path);

        Assert.True(bytes.Length > 22);
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));      // type: icon
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 4));      // one image
        Assert.Equal(32, bytes[6]);                            // width
        Assert.Equal(32, bytes[7]);                            // height

        uint size = BitConverter.ToUInt32(bytes, 14);
        uint offset = BitConverter.ToUInt32(bytes, 18);
        Assert.Equal(bytes.Length - 22u, size);
        Assert.Equal(22u, offset);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[22..26]); // PNG payload
    }

    [Fact]
    public void PropSpritesAreRealAlphaPngs()
    {
        foreach ((string? file, int width, int height) in new[]
                 {
                     ("bowl-empty.png", 24, 24),
                     ("bowl-full.png", 24, 24),
                     ("ball.png", 16, 16),
                 })
        {
            string path = Path.Combine(SpriteDirectory, file);
            Assert.True(File.Exists(path), $"Missing prop sprite: {path}");

            PngHeader header = ReadPngHeader(path);

            Assert.Equal(width, header.Width);
            Assert.Equal(height, header.Height);
            Assert.Equal(6, header.ColorType); // RGBA, not a checkerboard
        }
    }

    private readonly record struct PngHeader(int Width, int Height, int BitDepth, int ColorType);

    private static PngHeader ReadPngHeader(string path)
    {
        byte[] bytes = new byte[33];
        using FileStream stream = File.OpenRead(path);
        int read = stream.Read(bytes, 0, bytes.Length);

        Assert.Equal(33, read);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes.Take(8).ToArray());
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));

        int width = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        int height = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        return new PngHeader(width, height, bytes[24], bytes[25]);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Petty.Desktop.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
