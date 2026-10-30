using System.Text.Json;
using System.Text;

namespace Petty.Assets;

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
