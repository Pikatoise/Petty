using System.IO;
using System.Security.Cryptography;
using Petty.App.Sprites;
using Petty.Assets;
using Petty.Core.Assets;

internal static partial class Program
{
    private static void CheckMailAssets(string root, string embedded)
    {
        string mailRoot = Path.Combine(embedded, "mail-packs");
        Require(Directory.GetFiles(mailRoot, "*.json", SearchOption.AllDirectories).Length == 40,
            "Embedded release must contain forty mail atlases.");
        var hashes = new HashSet<string>();
        int count = 0;
        foreach (BreedDesign breed in BreedDesign.Load(Path.Combine(root, "assets", "breed-designs.json")))
        {
            foreach (CoatDesign coat in breed.Coats)
            {
                string id = breed.Id + "-" + coat.Id + "-mail";
                string directory = Path.Combine(mailRoot, breed.Id);
                SpriteSheet sheet = SpriteSheetLoader.Load(directory, id, mail: true);
                Require(sheet.Clips.Count == 4 && sheet.GroundRow == 53 && !sheet.Metadata.IsDefault,
                    "Invalid mail clips, ground or creator default.");
                Require(SpriteMetadataValidator.Validate(sheet.Metadata).Count > 0,
                    "Mail atlas must not pass as a regular pet.");
                ValidationReport report = AssetValidator.ValidateAtlas(directory, id, mail: true);
                Require(report.Passed, string.Join("; ", report.Errors));
                byte[] png = File.ReadAllBytes(Path.Combine(directory, id + ".png"));
                Require(hashes.Add(Convert.ToHexString(SHA256.HashData(png))), "Duplicate mail atlas.");
                byte[] generated = BreedPackBuilder.Build(breed, coat, mail: true).Atlas.ToPngBytes();
                Require(png.AsSpan().SequenceEqual(generated), $"Non-deterministic embedded mail atlas: {id}.");
                Require(png.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root, "assets", "sprites", "mail-packs", breed.Id, id + ".png"))),
                    "Embedded mail atlas differs from source.");
                foreach (SpriteClip clip in sheet.Clips.Values)
                {
                    var frames = new HashSet<string>();
                    foreach (SpriteFrame frame in clip.Frames)
                    {
                        Require(frame.Image.IsFrozen, "Mail frame must be frozen.");
                        byte[] pixels = new byte[64 * 64 * 4];
                        frame.Image.CopyPixels(pixels, 64 * 4, 0);
                        frames.Add(Convert.ToHexString(SHA256.HashData(pixels)));
                        int bottom = -1;
                        bool hasBag = false;
                        for (int y = 0; y < 64; y++)
                        {
                            for (int x = 0; x < 64; x++)
                            {
                                int index = (y * 64 + x) * 4;
                                bool opaque = pixels[index + 3] == 255;
                                Require(sheet.IsOpaque(frame.CellIndex, x, y) == opaque, "Mail hit mask differs from alpha.");
                                if (opaque)
                                {
                                    bottom = y;
                                }
                                hasBag |= pixels[index] == 0x61 && pixels[index + 1] == 0x9A && pixels[index + 2] == 0xCD && opaque;
                                int logical = ((y / 2 * 2) * 64 + x / 2 * 2) * 4;
                                Require(pixels.AsSpan(index, 4).SequenceEqual(pixels.AsSpan(logical, 4)), "Mail pixels leave the 2x logical grid.");
                            }
                        }
                        Require(bottom == 53 && hasBag, $"Mail ground or satchel missing: {id}/{clip.Metadata.Name}.");
                        count++;
                    }
                    Require(frames.Count >= 2, "Mail animation is static.");
                }
            }
        }
        Require(count == 1440, "Expected 1,440 frozen mail frames.");
    }
}
