using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Petty.Assets;

public sealed class Variant
{
    public string Id { get; set; } = "";
    public string Breed { get; set; } = "";
    public string Coat { get; set; } = "";
    public string? Source { get; set; }
    public string[] Palette { get; set; } = [];
    public uint[] Colors => Palette.Select(color => 0xFF000000u | uint.Parse(color, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();

    public static Variant[] Load(string path)
    {
        var variants = JsonSerializer.Deserialize<Variant[]>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Sprite catalog is empty.");
        if (variants.Length == 0 || variants.Any(v => v is null) || variants.Select(v => v.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != variants.Length)
            throw new InvalidDataException("Sprite catalog must contain unique variant IDs.");
        foreach (var v in variants)
        {
            if (v.Id is null || !Regex.IsMatch(v.Id, "^[a-z0-9]+(?:-[a-z0-9]+)*$") ||
                new[] { "catalog", "ball", "bowl-empty", "bowl-full", "tray-cat" }.Contains(v.Id, StringComparer.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(v.Breed) || string.IsNullOrWhiteSpace(v.Coat) ||
                (v.Source is not null && (string.IsNullOrWhiteSpace(v.Source) || Path.IsPathRooted(v.Source) || v.Source.Split('/', '\\').Any(part => part is ".." or "." or ""))) ||
                v.Palette is null || v.Palette.Length != Petty.Assets.Palette.All.Length || v.Palette.Any(c => c is null || !Regex.IsMatch(c, "^[0-9a-fA-F]{6}$")))
                throw new InvalidDataException($"Invalid sprite catalog entry: {v.Id}");
        }
        return variants;
    }
}
