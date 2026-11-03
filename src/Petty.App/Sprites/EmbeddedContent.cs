using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Petty.App.Sprites;

/// <summary>Keeps the existing file-based pack loader usable with a single-file release.</summary>
public static class EmbeddedContent
{
    private static readonly Assembly Assembly = typeof(EmbeddedContent).Assembly;
    private static readonly Lazy<string> SpriteDirectory = new(ExtractSprites);

    public static string Sprites => SpriteDirectory.Value;

    public static string Licenses => string.Join(Environment.NewLine + Environment.NewLine,
        Assembly.GetManifestResourceNames().Where(n => n.StartsWith("Licenses/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).Select(name =>
            {
                using var reader = new StreamReader(Assembly.GetManifestResourceStream(name)!);
                return name + Environment.NewLine + reader.ReadToEnd();
            }));

    private static string ExtractSprites()
    {
        string root = Path.Combine(Path.GetTempPath(), "Petty", "bundled", Assembly.ManifestModule.ModuleVersionId.ToString("N"));
        foreach (string name in Assembly.GetManifestResourceNames().Where(n => n.StartsWith("Sprites/", StringComparison.Ordinal)))
        {
            string path = Path.Combine(root, name["Sprites/".Length..].Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
            using Stream resource = Assembly.GetManifestResourceStream(name)!;
            using var content = new MemoryStream();
            resource.CopyTo(content);
            byte[] bytes = content.ToArray();
            if (File.Exists(path) && SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(bytes)))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        return root;
    }
}
