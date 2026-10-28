using static Petty.Assets.Palette;

namespace Petty.Assets;

/// <summary>
/// 32x32 cat head for the notification-area icon. Same palette and hard pixel edges
/// as the atlas; the tray is too small for the full side view.
/// </summary>
public static class TrayIconBuilder
{
    public const string ImageName = "tray-cat.png";
    public const string IconFileName = "petty.ico";
    public const int Size = 32;

    public static PixelCanvas Build()
    {
        var canvas = new PixelCanvas(Size, Size);

        // Ears first so the head ellipse covers their base.
        canvas.FillTriangle(6, 14, 10, 3, 16, 13, Orange);
        canvas.FillTriangle(16, 13, 22, 3, 26, 14, Orange);
        canvas.FillTriangle(9, 11, 10.5, 6, 13, 11, Pink);
        canvas.FillTriangle(19, 11, 21.5, 6, 23, 11, Pink);

        canvas.FillEllipse(16, 18, 10.5, 9.5, Orange);

        canvas.FillRect(11, 10, 3, 1, OrangeDark);
        canvas.FillRect(15, 9, 2, 1, OrangeDark);
        canvas.FillRect(18, 10, 3, 1, OrangeDark);

        canvas.FillEllipse(16, 22.5, 5.5, 3.4, Cream);

        canvas.FillRect(11, 16, 2, 3, Outline);
        canvas.FillRect(19, 16, 2, 3, Outline);
        canvas.FillRect(15, 21, 2, 1, Pink);
        canvas.FillRect(16, 23, 1, 1, Outline);

        canvas.Outline(Outline);

        // Whiskers after the outline pass, so they stay one pixel thick.
        canvas.FillRect(2, 21, 4, 1, Cream);
        canvas.FillRect(2, 24, 4, 1, Cream);
        canvas.FillRect(27, 21, 3, 1, Cream);
        canvas.FillRect(27, 24, 3, 1, Cream);

        return canvas;
    }

    public static void Write(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Build().SavePng(Path.Combine(outputDirectory, ImageName));
    }

    /// <summary>
    /// Single-image .ico (32x32 PNG payload, supported since Vista) for the executable and
    /// therefore the title bars. Derived from the same art as the tray icon.
    /// </summary>
    public static void WriteIcon(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var png = Build().ToPngBytes();
        var bytes = new byte[22 + png.Length];

        using var stream = new MemoryStream(bytes);
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);                // reserved
        writer.Write((ushort)1);                // icon
        writer.Write((ushort)1);                // one image
        writer.Write((byte)Size);               // width
        writer.Write((byte)Size);               // height
        writer.Write((byte)0);                  // palette colours
        writer.Write((byte)0);                  // reserved
        writer.Write((ushort)1);                // colour planes
        writer.Write((ushort)32);               // bits per pixel
        writer.Write((uint)png.Length);
        writer.Write((uint)22);                 // payload offset
        writer.Write(png);

        File.WriteAllBytes(Path.Combine(outputDirectory, IconFileName), bytes);
    }
}
