using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Petty.App.Sprites;

/// <summary>
/// A single decoded prop picture (bowl, ball) plus its alpha mask. Props live in their
/// own tiny layered windows; the mask is what makes their transparent pixels
/// click-through, exactly like the pet sprite.
/// </summary>
public sealed class PropImage
{
    private readonly byte[] _alpha;

    private PropImage(BitmapSource image, byte[] alpha, int lastOpaqueRow)
    {
        Image = image;
        _alpha = alpha;
        LastOpaqueRow = lastOpaqueRow;
    }

    public BitmapSource Image { get; }

    public int Width => Image.PixelWidth;

    public int Height => Image.PixelHeight;

    /// <summary>Row of the lowest opaque pixel, so a prop can be stood on the floor line.</summary>
    public int LastOpaqueRow { get; }

    public bool IsOpaqueAt(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height && _alpha[(y * Width) + x] != 0;

    public static PropImage Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Prop image '{path}' is missing.", path);

        BitmapSource image;
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
                throw new InvalidDataException($"'{path}' contains no bitmap frames.");

            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            image = converted;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException($"Prop image '{path}' could not be decoded: {ex.Message}", ex);
        }

        var stride = image.PixelWidth * 4;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);

        var alpha = new byte[image.PixelWidth * image.PixelHeight];
        for (var i = 0; i < alpha.Length; i++) alpha[i] = pixels[(i * 4) + 3];

        var lastOpaqueRow = 0;
        for (var y = 0; y < image.PixelHeight; y++)
        {
            for (var x = 0; x < image.PixelWidth; x++)
            {
                if (alpha[(y * image.PixelWidth) + x] == 0) continue;

                lastOpaqueRow = y;
                break;
            }
        }

        return new PropImage(image, alpha, lastOpaqueRow);
    }
}
