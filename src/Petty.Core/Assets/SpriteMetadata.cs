using System.Text.Json.Serialization;

namespace Petty.Core.Assets;

public sealed class AnimationClipMetadata
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Zero-based cell index in the atlas (row-major, row = index / Columns).</summary>
    public int StartIndex { get; set; }

    public int FrameCount { get; set; }

    public double FramesPerSecond { get; set; } = 8;

    public bool Loop { get; set; } = true;
}

public sealed class SpriteSheetMetadata
{
    public string Character { get; set; } = string.Empty;

    public string? BreedId { get; set; }

    public string? CoatId { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>File name of the atlas PNG relative to the metadata file.</summary>
    public string Image { get; set; } = string.Empty;

    public int FrameWidth { get; set; }

    public int FrameHeight { get; set; }

    public int Columns { get; set; }

    public int Rows { get; set; }

    /// <summary>Row inside each frame that touches the ground; the app uses it to align the pet.</summary>
    public int GroundRow { get; set; } = 52;

    public List<AnimationClipMetadata> Animations { get; set; } = new();

    [JsonIgnore]
    public int CellCount => Columns * Rows;
}

public static class SpriteMetadataValidator
{
    public const int MinFrameSize = 8;
    public const int MaxFrameSize = 512;

    public static readonly string[] RequiredClips =
    [
        "idle", "walk", "run", "eat", "sleep", "play", "tired", "exit",
    ];

    public static IReadOnlyList<string> Validate(SpriteSheetMetadata? metadata)
    {
        var errors = new List<string>();
        if (metadata is null)
        {
            errors.Add("Metadata is null.");
            return errors;
        }

        if (!IsValidId(metadata.Character)) errors.Add("Character id is invalid.");
        if (!string.Equals(metadata.Image, metadata.Character + ".png", StringComparison.Ordinal))
            errors.Add("Image must be the character ID followed by .png.");
        if (metadata.BreedId is not null && !IsValidId(metadata.BreedId)) errors.Add("Breed id is invalid.");
        if (metadata.CoatId is not null && !IsValidId(metadata.CoatId)) errors.Add("Coat id is invalid.");
        if (metadata.Animations is null || metadata.Animations.Any(clip => clip is null))
        {
            errors.Add("Animation clips are missing or invalid.");
            return errors;
        }

        if (metadata.FrameWidth != 64) errors.Add("FrameWidth must be 64.");
        if (metadata.FrameHeight != 64) errors.Add("FrameHeight must be 64.");
        if (metadata.Columns is <= 0 or > 256) errors.Add("Columns must be between 1 and 256.");
        if (metadata.Rows is <= 0 or > 256) errors.Add("Rows must be between 1 and 256.");
        if (metadata.GroundRow <= 0 || metadata.GroundRow >= metadata.FrameHeight)
            errors.Add($"GroundRow {metadata.GroundRow} must be inside the frame.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in metadata.Animations)
        {
            if (string.IsNullOrWhiteSpace(clip.Name))
            {
                errors.Add("An animation clip has no name.");
                continue;
            }

            if (!names.Add(clip.Name))
                errors.Add($"Duplicate animation clip '{clip.Name}'.");

            if (clip.FrameCount <= 0)
                errors.Add($"Clip '{clip.Name}' has no frames.");
            if (clip.StartIndex < 0)
                errors.Add($"Clip '{clip.Name}' has a negative start index.");
            if (clip.FrameCount > 0 && (long)clip.StartIndex + clip.FrameCount > (long)metadata.Columns * metadata.Rows)
                errors.Add($"Clip '{clip.Name}' runs past the atlas grid ({metadata.CellCount} cells).");
            if (!double.IsFinite(clip.FramesPerSecond) || clip.FramesPerSecond is <= 0 or > 60)
                errors.Add($"Clip '{clip.Name}' has an unusable frame rate ({clip.FramesPerSecond}).");
        }

        foreach (var required in RequiredClips)
        {
            if (!names.Contains(required))
                errors.Add($"Required animation '{required}' is missing.");
        }

        var ordered = metadata.Animations
            .Where(a => a.FrameCount > 0 && a.StartIndex >= 0)
            .OrderBy(a => a.StartIndex)
            .ToList();

        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];
            if ((long)current.StartIndex < (long)previous.StartIndex + previous.FrameCount)
                errors.Add($"Animations '{previous.Name}' and '{current.Name}' overlap in the atlas.");
        }

        return errors;
    }

    public static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id) && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>Row/column of a clip frame inside the atlas.</summary>
    public static (int Column, int Row) CellPosition(SpriteSheetMetadata metadata, int cellIndex)
    {
        if (metadata.Columns <= 0) throw new ArgumentOutOfRangeException(nameof(metadata));
        if (cellIndex < 0 || cellIndex >= metadata.CellCount) throw new ArgumentOutOfRangeException(nameof(cellIndex));
        return (cellIndex % metadata.Columns, cellIndex / metadata.Columns);
    }
}
