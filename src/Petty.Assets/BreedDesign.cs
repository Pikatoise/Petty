using System.Globalization;
using System.Text.Json;
using Petty.Core.Assets;

namespace Petty.Assets;

public sealed class BreedDesign
{
    public string Id { get; set; } = "";
    public string Species { get; set; } = "cat";
    public string Name { get; set; } = "";
    public int BodyX { get; set; }
    public int BodyY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int HeadX { get; set; }
    public int HeadY { get; set; }
    public string Head { get; set; } = "round";
    public string Ears { get; set; } = "small";
    public string Tail { get; set; } = "upright";
    public bool Fluffy { get; set; }
    public CoatDesign[] Coats { get; set; } = [];

    public static BreedDesign[] Load(string path)
    {
        var breeds = JsonSerializer.Deserialize<BreedDesign[]>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Missing breed designs.");
        if (breeds.Length == 0 || breeds.Any(b => b is null) || breeds.Select(b => b.Id).Distinct().Count() != breeds.Length)
            throw new InvalidDataException("Breed designs must have unique IDs.");
        foreach (var b in breeds)
        {
            if (!SpriteMetadataValidator.IsValidId(b.Id) || b.Species is not ("cat" or "dog") || string.IsNullOrWhiteSpace(b.Name) ||
                b.BodyX is < 6 or > 12 || b.BodyY is < 12 or > 19 || b.Width is < 12 or > 21 || b.Height is < 5 or > 11 ||
                b.BodyX + b.Width > 29 || b.BodyY + b.Height > 26 || b.HeadX is < 16 or > 21 || b.HeadY is < 5 or > 14 ||
                b.Head is not ("round" or "wedge" or "flat" or "muzzle") || b.Ears is not ("small" or "large" or "tuft" or "drop" or "semi") ||
                b.Tail is not ("upright" or "plume" or "straight" or "curl") ||
                b.Coats is null || b.Coats.Length != 5 || b.Coats.Any(c => c is null) || b.Coats.Select(c => c.Id).Distinct().Count() != 5)
                throw new InvalidDataException($"Invalid breed design: {b.Id}");
            foreach (var coat in b.Coats)
            {
                if (!SpriteMetadataValidator.IsValidId(coat.Id) || !CoatDesign.Patterns.Contains(coat.Pattern) ||
                    coat.Colors is null || coat.Colors.Length != 6 || coat.Colors.Any(c => c is null || c.Length != 6 || !uint.TryParse(c, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)))
                    throw new InvalidDataException($"Invalid coat: {b.Id}/{coat.Id}");
            }
        }
        return breeds;
    }
}

public sealed class CoatDesign
{
    public string Id { get; set; } = "";
    public string Pattern { get; set; } = "solid";
    // Fur, shadow, light, white, white shadow, marking/accent.
    public string[] Colors { get; set; } = [];
    public uint[] Palette => Colors.Select(c => 0xFF000000u | uint.Parse(c, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
    public static readonly HashSet<string> Patterns = ["solid", "bicolor", "tabby", "classic", "spotted", "smoke", "tortie", "point", "lynx", "shaded", "sable", "tricolor", "merle", "dapple", "brindle", "mask", "agouti", "piebald"];
}
