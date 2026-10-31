using System.Buffers.Binary;
using Petty.Core.Assets;

namespace Petty.Tests;

/// <summary>
/// Checks the asset files that actually ship in the repository, independent of the
/// generator: metadata consistency, PNG dimensions and a real RGBA alpha channel.
/// </summary>
public class SpriteAssetFileTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string SpriteDirectory = Path.Combine(RepositoryRoot, "assets", "sprites");
    private static readonly string MetadataPath = Path.Combine(SpriteDirectory, "cat-orange-white.json");

    [Fact]
    public void ShippedMetadataIsValidAndCoversEveryFrame()
    {
        Assert.True(File.Exists(MetadataPath), $"Missing metadata: {MetadataPath}");

        var metadata = System.Text.Json.JsonSerializer.Deserialize<SpriteSheetMetadata>(
            File.ReadAllText(MetadataPath),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.NotNull(metadata);
        Assert.Empty(SpriteMetadataValidator.Validate(metadata, requirePet: true));

        var totalFrames = metadata!.Animations.Sum(a => a.FrameCount);
        Assert.Equal(52, totalFrames);

        var expected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["idle"] = 4,
            ["walk"] = 8,
            ["run"] = 8,
            ["eat"] = 6,
            ["sleep"] = 4,
            ["play"] = 8,
            ["pet"] = 4,
            ["tired"] = 4,
            ["exit"] = 6,
        };

        foreach (var (name, count) in expected)
        {
            var clip = Assert.Single(metadata.Animations, a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(count, clip.FrameCount);
            Assert.True(clip.FramesPerSecond > 0);
        }

        Assert.False(metadata.Animations.Single(a => a.Name == "exit").Loop);
    }

    [Fact]
    public void AtlasPngMatchesMetadataAndHasRealAlpha()
    {
        var metadata = LoadMetadata();
        var atlasPath = Path.Combine(SpriteDirectory, metadata.Image);

        Assert.True(File.Exists(atlasPath), $"Missing atlas: {atlasPath}");

        var header = ReadPngHeader(atlasPath);

        Assert.Equal(8, header.BitDepth);
        Assert.Equal(6, header.ColorType); // truecolour with alpha
        Assert.Equal(metadata.Columns * metadata.FrameWidth, header.Width);
        Assert.Equal(metadata.Rows * metadata.FrameHeight, header.Height);
    }

    [Fact]
    public void CatalogVariantsHaveValidAtlases()
    {
        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(SpriteDirectory, "catalog.json")));
        foreach (var variant in catalog.RootElement.EnumerateArray())
        {
            var id = variant.GetProperty("id").GetString()!;
            var metadata = System.Text.Json.JsonSerializer.Deserialize<SpriteSheetMetadata>(
                File.ReadAllText(Path.Combine(SpriteDirectory, id + ".json")),
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            Assert.Equal(id, metadata.Character);
            Assert.Empty(SpriteMetadataValidator.Validate(metadata));
            var header = ReadPngHeader(Path.Combine(SpriteDirectory, metadata.Image));
            Assert.Equal(metadata.Columns * metadata.FrameWidth, header.Width);
            Assert.Equal(metadata.Rows * metadata.FrameHeight, header.Height);
            Assert.Equal(6, header.ColorType);
        }
    }

    [Fact]
    public void GroundRowSitsInsideTheFrame()
    {
        var metadata = LoadMetadata();
        Assert.InRange(metadata.GroundRow, 1, metadata.FrameHeight - 2);
    }

    [Fact]
    public void TrayIconIsACrisp32pxPng()
    {
        var path = Path.Combine(SpriteDirectory, "tray-cat.png");
        Assert.True(File.Exists(path), $"Missing tray icon: {path}");

        var header = ReadPngHeader(path);

        Assert.Equal(32, header.Width);
        Assert.Equal(32, header.Height);
        Assert.Equal(8, header.BitDepth);
        Assert.Equal(6, header.ColorType);
    }

    [Fact]
    public void WindowIconIsAWellFormed32pxIco()
    {
        var path = Path.Combine(SpriteDirectory, "petty.ico");
        Assert.True(File.Exists(path), $"Missing window icon: {path}");

        var bytes = File.ReadAllBytes(path);

        Assert.True(bytes.Length > 22);
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));      // type: icon
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 4));      // one image
        Assert.Equal(32, bytes[6]);                            // width
        Assert.Equal(32, bytes[7]);                            // height

        var size = BitConverter.ToUInt32(bytes, 14);
        var offset = BitConverter.ToUInt32(bytes, 18);
        Assert.Equal(bytes.Length - 22u, size);
        Assert.Equal(22u, offset);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[22..26]); // PNG payload
    }

    [Fact]
    public void PropSpritesAreRealAlphaPngs()
    {
        foreach (var (file, width, height) in new[]
                 {
                     ("bowl-empty.png", 24, 16),
                     ("bowl-full.png", 24, 16),
                     ("ball.png", 16, 16),
                 })
        {
            var path = Path.Combine(SpriteDirectory, file);
            Assert.True(File.Exists(path), $"Missing prop sprite: {path}");

            var header = ReadPngHeader(path);

            Assert.Equal(width, header.Width);
            Assert.Equal(height, header.Height);
            Assert.Equal(6, header.ColorType); // RGBA, not a checkerboard
        }
    }

    private static SpriteSheetMetadata LoadMetadata() =>
        System.Text.Json.JsonSerializer.Deserialize<SpriteSheetMetadata>(
            File.ReadAllText(MetadataPath),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

    private readonly record struct PngHeader(int Width, int Height, int BitDepth, int ColorType);

    private static PngHeader ReadPngHeader(string path)
    {
        var bytes = new byte[33];
        using var stream = File.OpenRead(path);
        var read = stream.Read(bytes, 0, bytes.Length);

        Assert.Equal(33, read);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes.Take(8).ToArray());
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));

        var width = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        return new PngHeader(width, height, bytes[24], bytes[25]);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Petty.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
