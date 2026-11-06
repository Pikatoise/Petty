using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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

    private static readonly (string Name, int Frames, int Fps, bool Loop)[] MailClips =
    [
        ("mail-walk", 8, 10, true), ("mail-sit", 8, 10, false),
        ("mail-idle", 8, 6, true), ("mail-exit", 12, 10, false),
    ];

    public static AtlasBuildResult Build(BreedDesign breed, CoatDesign coat, bool mail = false)
    {
        string id = breed.Id + "-" + coat.Id + (mail ? "-mail" : "");
        var metadata = new SpriteSheetMetadata
        {
            Character = id,
            Image = id + ".png",
            SpeciesId = breed.Species,
            BreedId = breed.Id,
            CoatId = coat.Id,
            DisplayName = breed.Name + " / " + coat.Id.Replace('-', ' '),
            IsDefault = !mail && ReferenceEquals(coat, breed.Coats[0]),
            PlayContactX = mail ? null : 58,
            PlayContactY = mail ? null : (Math.Max(20, breed.BodyY + breed.Height - 1) * 2) + 1,
            FrameWidth = 64,
            FrameHeight = 64,
            Columns = 8,
            Rows = mail ? 5 : 8,
            GroundRow = BreedSpriteRenderer.GroundRow,
        };
        var atlas = new PixelCanvas(512, mail ? 320 : 512);
        int frameCount = 0;
        foreach ((string Name, int Frames, int Fps, bool Loop) clip in mail ? MailClips : Clips)
        {
            int start = frameCount;
            var unique = new HashSet<string>();
            for (int i = 0; i < clip.Frames; i++)
            {
                PixelCanvas canvas = BreedSpriteRenderer.Render(breed, coat, clip.Name, i);
                unique.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(canvas.ToPngBytes())));
                int cell = frameCount++;
                canvas.DrawOnto(atlas, cell % 8 * 64, cell / 8 * 64);
            }
            if (unique.Count < 2)
            {
                throw new InvalidDataException($"Static animation: {id}/{clip.Name}");
            }

            metadata.Animations.Add(new AnimationClipMetadata
            {
                Name = clip.Name,
                StartIndex = start,
                FrameCount = clip.Frames,
                FramesPerSecond = clip.Fps,
                Loop = clip.Loop,
            });
        }
        return new AtlasBuildResult { Atlas = atlas, Metadata = metadata };
    }

    public static void Generate(BreedDesign[] breeds, string directory, bool regenerate, bool mail = false)
    {
        foreach (BreedDesign breed in breeds)
        {
            string pack = Path.Combine(directory, mail ? "mail-packs" : "packs", breed.Id);
            foreach (CoatDesign coat in breed.Coats)
            {
                string id = breed.Id + "-" + coat.Id + (mail ? "-mail" : "");
                if (!regenerate && File.Exists(Path.Combine(pack, id + ".png")) && File.Exists(Path.Combine(pack, id + ".json")))
                {
                    continue;
                }

                AtlasWriter.Write(Build(breed, coat, mail), pack);
                Console.WriteLine($"Generated {id}.");
            }
        }
    }

    public static void WritePreview(BreedDesign[] breeds, string directory, bool mail = false)
    {
        Directory.CreateDirectory(directory);
        // Five coats per row, full-size 3x display. This is preview UI, not sprite data.
        using var board = new Bitmap(1120, breeds.Length * 170 + 70);
        using var g = Graphics.FromImage(board);
        using var title = new Font("Consolas", 17, FontStyle.Bold);
        using var label = new Font("Consolas", 10);
        using var brush = new SolidBrush(Color.FromArgb(236, 228, 208));
        g.Clear(Color.FromArgb(30, 34, 44));
        g.DrawString(mail ? "PETTY / 40 MAIL APPEARANCES" : "PETTY / 8 BREEDS / 40 COATS", title, brush, 20, 15);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        for (int row = 0; row < breeds.Length; row++)
        {
            BreedDesign breed = breeds[row];
            int top = 60 + row * 170;
            g.DrawString(breed.Name, label, brush, 20, top + 6);
            for (int col = 0; col < breed.Coats.Length; col++)
            {
                CoatDesign coat = breed.Coats[col];
                using var stream = new MemoryStream(BreedSpriteRenderer.Render(breed, coat, mail ? "mail-idle" : "idle", 0).ToPngBytes());
                using var frame = Image.FromStream(stream);
                g.DrawImage(frame, new Rectangle(175 + col * 185, top - 10, 160, 160));
                g.DrawString(coat.Id, label, brush, 182 + col * 185, top + 140);
            }
        }
        board.Save(Path.Combine(directory, mail ? "mail-overview.png" : "breeds-overview.png"), ImageFormat.Png);
        var variants = breeds.SelectMany(b => b.Coats.Select(c => new
        {
            id = b.Id + "-" + c.Id,
            species = b.Species,
            png = "data:image/png;base64," + Convert.ToBase64String(Build(b, c, mail).Atlas.ToPngBytes()),
        }));
        string template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "preview.template.html"));
        if (mail)
        {
            template = template.Replace("SPRITE PREVIEW", "MAIL SPRITE PREVIEW")
                .Replace("all nine runtime clips", "four prebuilt mail clips");
        }
        int start = 0;
        var clips = (mail ? MailClips : Clips).Select(c => { int index = start; start += c.Frames; return new { name = c.Name, frames = c.Frames, fps = c.Fps, start = index }; }).ToArray();
        File.WriteAllText(Path.Combine(directory, mail ? "mail-preview.html" : "preview.html"), template.Replace("/*MANIFEST*/", JsonSerializer.Serialize(new
        {
            clips,
            variants
        })));
    }
}
