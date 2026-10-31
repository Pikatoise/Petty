using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using Petty.Core.Assets;

namespace Petty.Assets;

public static class BreedPackBuilder
{
    private static readonly (string Name, int Frames, int Fps, bool Loop)[] Clips =
    [
        ("idle", 8, 6, true), ("walk", 8, 10, true), ("run", 8, 14, true),
        ("eat", 6, 7, true), ("sleep", 4, 3, true), ("play", 12, 10, true),
        ("pet", 6, 5, true), ("tired", 4, 4, true), ("exit", 8, 10, false),
    ];

    public static AtlasBuildResult Build(BreedDesign breed, CoatDesign coat)
    {
        var id = breed.Id + "-" + coat.Id;
        var metadata = new SpriteSheetMetadata
        {
            Character = id, Image = id + ".png", SpeciesId = breed.Species,
            BreedId = breed.Id, CoatId = coat.Id, DisplayName = breed.Name + " / " + coat.Id.Replace('-', ' '),
            IsDefault = ReferenceEquals(coat, breed.Coats[0]),
            PlayContactX = 58, PlayContactY = (Math.Max(20, breed.BodyY + breed.Height - 1) * 2) + 1,
            FrameWidth = 64, FrameHeight = 64, Columns = 8, Rows = 8,
            GroundRow = BreedSpriteRenderer.GroundRow,
        };
        var atlas = new PixelCanvas(512, 512);
        var frames = new List<SpriteFrame>();
        foreach (var clip in Clips)
        {
            var start = frames.Count;
            var unique = new HashSet<string>();
            for (var i = 0; i < clip.Frames; i++)
            {
                var canvas = BreedSpriteRenderer.Render(breed, coat, clip.Name, i);
                unique.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(canvas.ToPngBytes())));
                var cell = frames.Count;
                canvas.DrawOnto(atlas, cell % 8 * 64, cell / 8 * 64);
                frames.Add(new SpriteFrame(clip.Name, i, cell, canvas));
            }
            if (unique.Count < 2) throw new InvalidDataException($"Static animation: {id}/{clip.Name}");
            metadata.Animations.Add(new AnimationClipMetadata
            {
                Name = clip.Name, StartIndex = start, FrameCount = clip.Frames,
                FramesPerSecond = clip.Fps, Loop = clip.Loop,
            });
        }
        return new AtlasBuildResult { Atlas = atlas, Metadata = metadata, Frames = frames };
    }

    public static void Generate(BreedDesign[] breeds, string directory, bool regenerate)
    {
        foreach (var breed in breeds)
        {
            var pack = Path.Combine(directory, "packs", breed.Id);
            foreach (var coat in breed.Coats)
            {
                var id = breed.Id + "-" + coat.Id;
                if (!regenerate && File.Exists(Path.Combine(pack, id + ".png")) && File.Exists(Path.Combine(pack, id + ".json"))) continue;
                AtlasWriter.Write(Build(breed, coat), pack);
                Console.WriteLine($"Generated {id}.");
            }
            var note = $"# {breed.Name}\n\nOriginal procedural C# pixel art, derived from experiments/pixel-pets.\n" +
                "No third-party sprite pixels or AI-generated art. Source: assets/breed-designs.json and BreedSpriteRenderer.\n" +
                "64px cells, 32px logical grid, nearest-neighbour 2x, binary alpha, ground row 53.\n" +
                "Nine shared clips including petting; 64 frames per coat. First coat is the creator default.\n\n" +
                string.Join("\n", breed.Coats.Select(c => $"- `{breed.Id}-{c.Id}`: {c.Pattern}")) +
                "\n\nStylized anatomy; species share gait structure with breed-specific proportions, ears, tails and head shapes.\n" +
                "Visual review of motion/ball contact and DPI remains an owner check.\n";
            AtlasWriter.WriteIfChanged(Path.Combine(pack, "PACK_NOTES.md"), Encoding.UTF8.GetBytes(note));
        }
    }

    public static void WritePreview(BreedDesign[] breeds, string directory)
    {
        Directory.CreateDirectory(directory);
        // Five coats per row, full-size 3x display. This is preview UI, not sprite data.
        using var board = new Bitmap(1120, breeds.Length * 170 + 70);
        using var g = Graphics.FromImage(board);
        using var title = new Font("Consolas", 17, FontStyle.Bold);
        using var label = new Font("Consolas", 10);
        using var brush = new SolidBrush(Color.FromArgb(236, 228, 208));
        g.Clear(Color.FromArgb(30, 34, 44));
        g.DrawString("PETTY / 8 BREEDS / 40 COATS", title, brush, 20, 15);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        for (var row = 0; row < breeds.Length; row++)
        {
            var breed = breeds[row];
            var top = 60 + row * 170;
            g.DrawString(breed.Name, label, brush, 20, top + 6);
            for (var col = 0; col < breed.Coats.Length; col++)
            {
                var coat = breed.Coats[col];
                using var stream = new MemoryStream(BreedSpriteRenderer.Render(breed, coat, "idle", 0).ToPngBytes());
                using var frame = Image.FromStream(stream);
                g.DrawImage(frame, new Rectangle(175 + col * 185, top - 10, 160, 160));
                g.DrawString(coat.Id, label, brush, 182 + col * 185, top + 140);
            }
        }
        board.Save(Path.Combine(directory, "breeds-overview.png"), ImageFormat.Png);
        var variants = breeds.SelectMany(b => b.Coats.Select(c => new
        {
            id = b.Id + "-" + c.Id, species = b.Species,
            png = "data:image/png;base64," + Convert.ToBase64String(Build(b, c).Atlas.ToPngBytes()),
        }));
        // The standalone viewer accepts both row-based experiments and contiguous runtime clips.
        var template = File.ReadAllText(Path.Combine("experiments", "pixel-pets", "preview.template.html"));
        template = template.Replace("Isolated experiment, no connection to the app.", "Production content preview: 8 breeds, 40 coats.");
        template = template.Replace("Limited proof of concept: fixed silhouettes per species, selected poses, no app-ready content pack.", "Procedural breed silhouettes and patterned coats; all nine runtime clips.");
        var start = 0;
        var clips = Clips.Select(c => { var index = start; start += c.Frames; return new { name = c.Name, frames = c.Frames, fps = c.Fps, start = index }; }).ToArray();
        File.WriteAllText(Path.Combine(directory, "preview.html"), template.Replace("/*MANIFEST*/", JsonSerializer.Serialize(new { clips, variants })));
    }
}
