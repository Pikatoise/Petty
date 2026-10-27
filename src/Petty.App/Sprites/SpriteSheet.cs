using System.IO;
using System.Windows.Media.Imaging;
using Petty.Core.Assets;

namespace Petty.App.Sprites;

public sealed record SpriteFrame(int CellIndex, int ClipFrameIndex, BitmapSource Image)
{
    /// <summary>Column/row of the frame inside the atlas, for diagnostics.</summary>
    public int Column { get; init; }

    public int Row { get; init; }
}

public sealed record SpriteClip(
    AnimationClipMetadata Metadata,
    IReadOnlyList<SpriteFrame> Frames)
{
    public string Name => Metadata.Name;

    public double FramesPerSecond => Metadata.FramesPerSecond;

    public bool Loop => Metadata.Loop;

    public SpriteFrame this[int index] => Frames[index];
}

/// <summary>
/// Decoded sprite atlas. Frames are cropped once and frozen, so switching frames
/// only swaps a cached <see cref="BitmapSource"/> reference — no PNG decoding or
/// per-frame allocations during playback.
/// </summary>
public sealed class SpriteSheet
{
    private const byte AlphaThreshold = 0;

    private readonly byte[] _alpha;
    private readonly int _atlasWidth;
    private readonly Dictionary<string, SpriteClip> _clips;

    internal SpriteSheet(
        SpriteSheetMetadata metadata,
        int atlasWidth,
        byte[] alpha,
        Dictionary<string, SpriteClip> clips)
    {
        Metadata = metadata;
        _atlasWidth = atlasWidth;
        _alpha = alpha;
        _clips = clips;
    }

    public SpriteSheetMetadata Metadata { get; }

    public IReadOnlyDictionary<string, SpriteClip> Clips => _clips;

    public int FrameWidth => Metadata.FrameWidth;

    public int FrameHeight => Metadata.FrameHeight;

    public int GroundRow => Metadata.GroundRow;

    public bool TryGetClip(string name, out SpriteClip clip) => _clips.TryGetValue(name, out clip!);

    public SpriteClip GetClip(string name) =>
        _clips.TryGetValue(name, out var clip)
            ? clip
            : throw new KeyNotFoundException($"Animation clip '{name}' is not present in the atlas.");

    /// <summary>Alpha test against the original, unsmoothed sprite pixel grid.</summary>
    public bool IsOpaque(int cellIndex, int spriteX, int spriteY)
    {
        if (spriteX < 0 || spriteY < 0 || spriteX >= FrameWidth || spriteY >= FrameHeight) return false;
        if (cellIndex < 0 || cellIndex >= Metadata.CellCount) return false;

        var (column, row) = SpriteMetadataValidator.CellPosition(Metadata, cellIndex);
        var x = (column * FrameWidth) + spriteX;
        var y = (row * FrameHeight) + spriteY;
        return _alpha[(y * _atlasWidth) + x] > AlphaThreshold;
    }
}
