namespace Petty.Core.Assets;

/// <summary>Validates decoded BGRA pixels before frames are exposed to the renderer.</summary>
public static class SpritePixels
{
    public static bool HasRgbaPngHeader(Stream stream)
    {
        Span<byte> header = stackalloc byte[26];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return read == header.Length && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && header[12..16].SequenceEqual("IHDR"u8) && header[24] == 8 && header[25] == 6;
    }

    public static IReadOnlyList<string> Validate(SpriteSheetMetadata metadata, byte[] pixels)
    {
        var errors = new List<string>();
        var width = metadata.Columns * metadata.FrameWidth;
        var height = metadata.Rows * metadata.FrameHeight;
        if ((long)width * height * 4 != pixels.LongLength) return ["Pixel buffer size does not match the atlas grid."];
        var occupied = new HashSet<int>();
        var standing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "idle", "walk", "eat", "tired", "exit" };
        foreach (var clip in metadata.Animations)
        {
            var clipBottom = -1;
            for (var i = 0; i < clip.FrameCount; i++)
            {
                var cell = clip.StartIndex + i;
                occupied.Add(cell);
                var x0 = cell % metadata.Columns * metadata.FrameWidth;
                var y0 = cell / metadata.Columns * metadata.FrameHeight;
                var count = 0;
                for (var y = 0; y < metadata.FrameHeight; y++)
                    for (var x = 0; x < metadata.FrameWidth; x++)
                    {
                        var alpha = pixels[((y0 + y) * width + x0 + x) * 4 + 3];
                        if (alpha == 0) continue;
                        if (alpha != 255) return [$"Clip '{clip.Name}' contains semi-transparent pixels."];
                        if (x == 0 || y == 0 || x == metadata.FrameWidth - 1 || y == metadata.FrameHeight - 1)
                            return [$"Clip '{clip.Name}' frame {i} touches a cell edge."];
                        count++;
                        clipBottom = Math.Max(clipBottom, y);
                    }
                if (count == 0) errors.Add($"Clip '{clip.Name}' frame {i} is empty.");
            }
            if (standing.Contains(clip.Name) && clipBottom != metadata.GroundRow)
                errors.Add($"Clip '{clip.Name}' baseline is row {clipBottom}, expected {metadata.GroundRow}.");
        }
        for (var cell = 0; cell < metadata.CellCount; cell++)
        {
            if (occupied.Contains(cell)) continue;
            var x0 = cell % metadata.Columns * metadata.FrameWidth;
            var y0 = cell / metadata.Columns * metadata.FrameHeight;
            for (var y = 0; y < metadata.FrameHeight; y++)
                for (var x = 0; x < metadata.FrameWidth; x++)
                    if (pixels[((y0 + y) * width + x0 + x) * 4 + 3] != 0)
                        return ["Unused atlas cells must be transparent."];
        }
        return errors;
    }
}
