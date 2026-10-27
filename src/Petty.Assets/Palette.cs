namespace Petty.Assets;

/// <summary>Deliberately small palette; every opaque pixel must use one of these colours.</summary>
public static class Palette
{
    public const uint Transparent = 0x00000000;
    public const uint Outline = 0xFF2B1A12;
    public const uint Orange = 0xFFE8892B;
    public const uint OrangeDark = 0xFFC56A1D;
    public const uint Cream = 0xFFFFF3DC;
    public const uint CreamShade = 0xFFE9D8B9;
    public const uint Pink = 0xFFE77C7C;
    public const uint Extra = 0xFF8FA6C4;

    public static readonly uint[] All =
    [
        Outline, Orange, OrangeDark, Cream, CreamShade, Pink, Extra,
    ];

    public static readonly HashSet<uint> Set = new(All);
}
