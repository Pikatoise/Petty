using System.Text;
using System.Text.Json;

namespace Petty.Assets;

public sealed record SpriteFrame(string Clip, int FrameIndex, int CellIndex, PixelCanvas Canvas);

public sealed class AtlasBuildResult
{
    public required PixelCanvas Atlas { get; init; }
    public required Petty.Core.Assets.SpriteSheetMetadata Metadata { get; init; }
    public required IReadOnlyList<SpriteFrame> Frames { get; init; }
}

public static class AtlasWriter
{
    public static void Write(AtlasBuildResult build, string directory)
    {
        Directory.CreateDirectory(directory);
        WriteIfChanged(Path.Combine(directory, build.Metadata.Image), build.Atlas.ToPngBytes());
        WriteIfChanged(Path.Combine(directory, build.Metadata.Character + ".json"), Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(build.Metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })));
    }

    public static void WriteIfChanged(string path, byte[] bytes)
    {
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            File.WriteAllBytes(path, bytes);
    }
}
