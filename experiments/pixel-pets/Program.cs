using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using Petty.Assets;
using PixelPets;

var output = Path.GetFullPath(args.Length > 0 ? args[0] : "experiments/pixel-pets/output");
Directory.CreateDirectory(output);
var clips = new[]
{
    new { Name = "idle", Frames = 8, Fps = 6 },
    new { Name = "walk", Frames = 8, Fps = 10 },
    new { Name = "run", Frames = 8, Fps = 14 },
    new { Name = "sit", Frames = 8, Fps = 5 },
    new { Name = "eat", Frames = 6, Fps = 7 },
    new { Name = "sleep", Frames = 4, Fps = 3 },
    new { Name = "play", Frames = 8, Fps = 9 },
    new { Name = "stretch", Frames = 8, Fps = 6 },
};
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var variants = new List<object>();
var verified = 0;
foreach (var coat in PetRenderer.Coats)
{
    var atlas = new PixelCanvas(64 * 8, 64 * clips.Length);
    for (var row = 0; row < clips.Length; row++)
    {
        var unique = new HashSet<string>();
        for (var frame = 0; frame < clips[row].Frames; frame++)
        {
            var canvas = PetRenderer.Render(coat, clips[row].Name, frame);
            Verify(canvas);
            if (!canvas.Pixels.SequenceEqual(PetRenderer.Render(coat, clips[row].Name, frame).Pixels))
                throw new InvalidOperationException("Nondeterministic frame.");
            unique.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(canvas.ToPngBytes())));
            canvas.DrawOnto(atlas, frame * 64, row * 64);
            verified++;
        }
        if (unique.Count < 2) throw new InvalidOperationException($"Static clip: {coat.Id}/{clips[row].Name}");
    }
    var path = Path.Combine(output, coat.Id + ".png");
    atlas.SavePng(path);
    var decoded = PixelCanvas.LoadPng(path);
    if (!decoded.Pixels.SequenceEqual(atlas.Pixels)) throw new InvalidOperationException("PNG round-trip mismatch.");
    variants.Add(new { id = coat.Id, species = coat.Dog ? "dog" : "cat", png = "data:image/png;base64," + Convert.ToBase64String(atlas.ToPngBytes()) });
}
var manifest = new
{
    experimental = true, frameWidth = 64, frameHeight = 64, logicalPixelSize = 2,
    columns = 8, rows = clips.Length, groundRow = 53,
    clips = clips.Select((clip, row) => new { name = clip.Name, row, frames = clip.Frames, fps = clip.Fps }),
    variants,
};
var data = JsonSerializer.Serialize(manifest, jsonOptions);
File.WriteAllText(Path.Combine(output, "manifest.json"), data);
var template = Path.Combine(AppContext.BaseDirectory, "preview.template.html");
File.WriteAllText(Path.Combine(output, "preview.html"), File.ReadAllText(template).Replace("/*MANIFEST*/", data));

// An overview is also usable without a browser; labels are preview UI, never sprite pixels.
using (var board = new Bitmap(960, 650))
using (var g = Graphics.FromImage(board))
using (var title = new Font("Consolas", 18, FontStyle.Bold))
using (var label = new Font("Consolas", 10))
using (var brush = new SolidBrush(Color.FromArgb(232, 226, 210)))
{
    g.Clear(Color.FromArgb(30, 34, 44));
    g.DrawString("PIXEL PETS / GENERATOR STUDY", title, brush, 24, 18);
    g.DrawString("Original procedural sprites / 64 x 64 cells / nearest neighbour", label, brush, 24, 52);
    g.InterpolationMode = InterpolationMode.NearestNeighbor;
    g.PixelOffsetMode = PixelOffsetMode.Half;
    for (var column = 0; column < 8; column++)
        g.DrawString(clips[column].Name.ToUpperInvariant(), label, brush, 160 + column * 98, 90);
    for (var row = 0; row < PetRenderer.Coats.Length; row++)
    {
        var coat = PetRenderer.Coats[row];
        var top = 118 + row * 124;
        g.DrawString(coat.Id.Replace('-', '\n'), label, brush, 24, top + 22);
        for (var col = 0; col < clips.Length; col++)
        {
            var frame = PetRenderer.Render(coat, clips[col].Name, 0);
            using var stream = new MemoryStream(frame.ToPngBytes());
            using var image = Image.FromStream(stream);
            g.DrawImage(image, new Rectangle(150 + col * 98, top, 96, 96));
        }
    }
    g.DrawString("Experimental only. Not connected to Petty. Open preview.html for motion + frame stepping.", label, brush, 24, 620);
    board.Save(Path.Combine(output, "overview.png"), ImageFormat.Png);
}
Console.WriteLine($"Verified {verified} frames: bounds, binary alpha, nonempty, animated clips, deterministic render, PNG round-trip.");
Console.WriteLine(Path.Combine(output, "preview.html"));

static void Verify(PixelCanvas frame)
{
    var count = 0;
    for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
        {
            var alpha = frame[x, y] >> 24;
            if (alpha is not (0 or 255)) throw new InvalidOperationException("Partial alpha.");
            if (alpha == 0) continue;
            count++;
            if (x < 2 || x >= 62 || y < 2 || y >= 56) throw new InvalidOperationException($"Out of bounds: {x},{y}");
        }
    if (count < 100) throw new InvalidOperationException("Empty frame.");
}
