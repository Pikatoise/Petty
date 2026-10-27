using Petty.Core.Assets;

namespace Petty.Assets;

public sealed record SpriteFrame(string Clip, int FrameIndex, int CellIndex, PixelCanvas Canvas);

public sealed class AtlasBuildResult
{
    public required PixelCanvas Atlas { get; init; }
    public required SpriteSheetMetadata Metadata { get; init; }
    public required IReadOnlyList<SpriteFrame> Frames { get; init; }
}

/// <summary>
/// Lays every animation out on one uniform grid: 8 columns x 6 rows of 64x64 cells.
/// Cell order is row-major and each clip owns a contiguous run of cells.
/// </summary>
public static class AtlasBuilder
{
    public const int Columns = 8;
    public const int Rows = 6;
    public const string CharacterId = "cat-orange-white";
    public const string ImageName = "cat-orange-white.png";
    public const string MetadataName = "cat-orange-white.json";

    public static AtlasBuildResult Build()
    {
        var definitions = Animations.CreateAll();
        var frameSize = CatSpriteRenderer.FrameSize;
        var atlas = new PixelCanvas(Columns * frameSize, Rows * frameSize);

        var metadata = new SpriteSheetMetadata
        {
            Character = CharacterId,
            Image = ImageName,
            FrameWidth = frameSize,
            FrameHeight = frameSize,
            Columns = Columns,
            Rows = Rows,
            GroundRow = CatSpriteRenderer.GroundRow,
        };

        var frames = new List<SpriteFrame>(48);
        var cell = 0;

        foreach (var definition in definitions)
        {
            var start = cell;

            for (var i = 0; i < definition.Poses.Count; i++)
            {
                if (cell >= Columns * Rows)
                    throw new InvalidOperationException($"Atlas grid is too small for animation '{definition.Name}'.");

                var canvas = CatSpriteRenderer.Render(definition.Poses[i]);
                var column = cell % Columns;
                var row = cell / Columns;
                canvas.DrawOnto(atlas, column * frameSize, row * frameSize);

                frames.Add(new SpriteFrame(definition.Name, i, cell, canvas));
                cell++;
            }

            metadata.Animations.Add(new AnimationClipMetadata
            {
                Name = definition.Name,
                StartIndex = start,
                FrameCount = definition.Poses.Count,
                FramesPerSecond = definition.FramesPerSecond,
                Loop = definition.Loop,
            });
        }

        return new AtlasBuildResult { Atlas = atlas, Metadata = metadata, Frames = frames };
    }
}
