using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Petty.Assets;

/// <summary>
/// Tiny ARGB pixel buffer with hard-edged primitives. Everything is drawn by
/// sampling pixel centres, so shapes never produce anti-aliased or semi-transparent
/// pixels — a hard requirement for the sprite art.
/// </summary>
public sealed class PixelCanvas
{
    private readonly uint[] _pixels;

    public PixelCanvas(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        _pixels = new uint[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlySpan<uint> Pixels => _pixels;

    public uint this[int x, int y]
    {
        get => InBounds(x, y) ? _pixels[(y * Width) + x] : Transparent;
        set
        {
            if (InBounds(x, y)) _pixels[(y * Width) + x] = value;
        }
    }

    private static uint Transparent => Palette.Transparent;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public bool IsOpaque(int x, int y) => (this[x, y] & 0xFF000000) != 0;

    public void FillRect(int x, int y, int width, int height, uint color)
    {
        for (var yy = y; yy < y + height; yy++)
            for (var xx = x; xx < x + width; xx++)
                this[xx, yy] = color;
    }

    public void FillEllipse(double cx, double cy, double rx, double ry, uint color)
    {
        if (rx <= 0 || ry <= 0) return;

        var minY = Math.Max(0, (int)Math.Floor(cy - ry));
        var maxY = Math.Min(Height - 1, (int)Math.Ceiling(cy + ry));

        for (var y = minY; y <= maxY; y++)
        {
            var dy = (y + 0.5 - cy) / ry;
            if (Math.Abs(dy) > 1) continue;

            var half = rx * Math.Sqrt(1 - (dy * dy));
            var minX = Math.Max(0, (int)Math.Ceiling(cx - half - 0.5));
            var maxX = Math.Min(Width - 1, (int)Math.Floor(cx + half - 0.5));

            for (var x = minX; x <= maxX; x++) _pixels[(y * Width) + x] = color;
        }
    }

    public void FillTriangle(
        double ax, double ay,
        double bx, double by,
        double cx, double cy,
        uint color)
    {
        var minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
        var maxX = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
        var minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
        var maxY = Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5;
                var py = y + 0.5;

                var d1 = Edge(px, py, ax, ay, bx, by);
                var d2 = Edge(px, py, bx, by, cx, cy);
                var d3 = Edge(px, py, cx, cy, ax, ay);

                var hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
                var hasPositive = d1 > 0 || d2 > 0 || d3 > 0;

                if (!(hasNegative && hasPositive)) _pixels[(y * Width) + x] = color;
            }
        }
    }

    public void StrokeLine(double x0, double y0, double x1, double y1, double thickness, uint color)
    {
        var radius = Math.Max(0.5, thickness / 2.0);
        var minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - radius));
        var maxX = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(x0, x1) + radius));
        var minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - radius));
        var maxY = Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(y0, y1) + radius));

        var dx = x1 - x0;
        var dy = y1 - y0;
        var lengthSquared = (dx * dx) + (dy * dy);

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5;
                var py = y + 0.5;

                double t = lengthSquared <= 0 ? 0 : (((px - x0) * dx) + ((py - y0) * dy)) / lengthSquared;
                t = Math.Clamp(t, 0, 1);

                var nearestX = x0 + (t * dx);
                var nearestY = y0 + (t * dy);
                var distX = px - nearestX;
                var distY = py - nearestY;

                if ((distX * distX) + (distY * distY) <= radius * radius)
                    _pixels[(y * Width) + x] = color;
            }
        }
    }

    public void Outline(uint color)
    {
        var snapshot = (uint[])_pixels.Clone();

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var index = (y * Width) + x;
                if ((snapshot[index] & 0xFF000000) != 0) continue;
                if (HasOpaqueNeighbour(snapshot, x, y)) _pixels[index] = color;
            }
        }
    }

    public PixelCanvas Upscale(int factor)
    {
        if (factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));

        var result = new PixelCanvas(Width * factor, Height * factor);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var color = this[x, y];
                if (color == 0) continue;

                for (var oy = 0; oy < factor; oy++)
                    for (var ox = 0; ox < factor; ox++)
                        result[(x * factor) + ox, (y * factor) + oy] = color;
            }
        }

        return result;
    }

    /// <summary>Alpha-composites this canvas onto an opaque target (used for preview sheets).</summary>
    public void DrawOnto(PixelCanvas target, int offsetX, int offsetY)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var color = this[x, y];
                var alpha = (color >> 24) & 0xFF;
                if (alpha == 0) continue;

                if (alpha == 255)
                {
                    target[offsetX + x, offsetY + y] = color;
                    continue;
                }

                var dst = target[offsetX + x, offsetY + y];
                var a = alpha / 255.0;
                var r = (byte)(((color >> 16) & 0xFF) * a + ((dst >> 16) & 0xFF) * (1 - a));
                var g = (byte)(((color >> 8) & 0xFF) * a + ((dst >> 8) & 0xFF) * (1 - a));
                var b = (byte)((color & 0xFF) * a + (dst & 0xFF) * (1 - a));
                target[offsetX + x, offsetY + y] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            }
        }
    }

    public void FillBackground(uint color) => Array.Fill(_pixels, color);

    public void SavePng(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, Width, Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        var buffer = new int[_pixels.Length];
        Buffer.BlockCopy(_pixels, 0, buffer, 0, _pixels.Length * 4);
        Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        bitmap.UnlockBits(data);

        bitmap.Save(path, ImageFormat.Png);
    }

    public static PixelCanvas LoadPng(string path)
    {
        using var bitmap = new Bitmap(path);
        var canvas = new PixelCanvas(bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        var buffer = new int[bitmap.Width * bitmap.Height];
        Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
        bitmap.UnlockBits(data);

        Buffer.BlockCopy(buffer, 0, canvas._pixels, 0, buffer.Length * 4);
        return canvas;
    }

    private bool HasOpaqueNeighbour(uint[] pixels, int x, int y)
    {
        for (var oy = -1; oy <= 1; oy++)
        {
            for (var ox = -1; ox <= 1; ox++)
            {
                if (ox == 0 && oy == 0) continue;

                var nx = x + ox;
                var ny = y + oy;
                if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                if ((pixels[(ny * Width) + nx] & 0xFF000000) != 0) return true;
            }
        }

        return false;
    }

    private static double Edge(double px, double py, double ax, double ay, double bx, double by) =>
        ((px - bx) * (ay - by)) - ((ax - bx) * (py - by));
}
