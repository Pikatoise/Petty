using static Petty.Assets.Palette;

namespace Petty.Assets;

/// <summary>
/// Food bowl and ball. They are drawn by the app into their own windows, never baked
/// into pet frames, so the cat's animations stay reusable while the props are added,
/// removed or moved at runtime.
///
/// Bowl frames share a fixed size and floor baseline. The food mound grows above
/// the rim; each displayed frame supplies its own click-through mask.
/// </summary>
public static class PropSpriteBuilder
{
    public const int BowlWidth = 24;
    public const int BowlHeight = 24;
    public const string BowlEmptyName = "bowl-empty.png";
    public const string BowlFullName = "bowl-full.png";

    public const int BallSize = 16;
    public const string BallName = "ball.png";

    public static PixelCanvas BuildBowl(int level)
    {
        var canvas = new PixelCanvas(BowlWidth, BowlHeight);

        canvas.FillEllipse(12, 16.5, 9.5, 6, Extra);
        canvas.FillRect(10, 21, 4, 1, CreamShade);
        canvas.FillEllipse(12, 13, 10, 3, Cream);
        canvas.FillEllipse(12, 13, 7.5, 1.8, Outline);

        if (level > 0)
        {
            // A stepped mound rises from the bowl as the inventory increases.
            var top = 12 - (level - 1) * 3;
            for (var y = top; y <= 13; y++)
            {
                var halfWidth = Math.Min(6, 2 + (y - top));
                canvas.FillRect(12 - halfWidth, y, halfWidth * 2 + 1, 1, OrangeDark);
                if (halfWidth > 2) canvas.FillRect(13 - halfWidth, y, halfWidth * 2 - 1, 1, Orange);
            }
            if (level >= 2) canvas[11, top + 1] = CreamShade;
            if (level >= 3) canvas[15, top + 4] = CreamShade;
            if (level >= 4) canvas[9, top + 6] = CreamShade;
        }

        canvas.Outline(Outline);
        return canvas;
    }

    public static PixelCanvas BuildPour(int frame)
    {
        var canvas = new PixelCanvas(24, 32);
        var y = frame == 0 ? 1 : 3;
        canvas.FillRect(11, y + 1, 8, 10, Cream);
        canvas.FillRect(12, y, 6, 1, OrangeDark);
        canvas.FillRect(12, y + 3, 6, 3, Orange);
        canvas.FillRect(11, y + 9, 8, 2, OrangeDark);
        canvas.Outline(Outline);
        if (frame is >= 1 and <= 4)
        {
            for (var i = 0; i < 4; i++)
            {
                var dropY = 14 + ((frame * 4 + i * 5) % 17);
                canvas.FillRect(10 - i % 2, dropY, 2, 2, i % 2 == 0 ? Orange : CreamShade);
            }
        }
        return canvas;
    }

    public static PixelCanvas BuildBall()
    {
        var canvas = new PixelCanvas(BallSize, BallSize);

        // A single highlight reads as a ball; a band across the middle looks like a sign.
        canvas.FillEllipse(8, 8, 6.5, 6.5, Pink);
        canvas.FillEllipse(6, 6, 2.5, 1.8, Cream);
        canvas.Outline(Outline);

        return canvas;
    }

    public static PixelCanvas BuildHand()
    {
        var canvas = new PixelCanvas(16, 16);
        canvas.FillRect(5, 2, 3, 8, Cream);
        canvas.FillRect(8, 4, 3, 7, Cream);
        canvas.FillRect(11, 6, 2, 6, Cream);
        canvas.FillRect(3, 7, 3, 5, Cream);
        canvas.FillRect(5, 9, 8, 5, Cream);
        canvas.Outline(Outline);
        return canvas;
    }

    public static void Write(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        BuildBowl(0).SavePng(Path.Combine(outputDirectory, BowlEmptyName));
        for (var level = 1; level <= 3; level++)
            BuildBowl(level).SavePng(Path.Combine(outputDirectory, $"bowl-{level * 25}.png"));
        BuildBowl(4).SavePng(Path.Combine(outputDirectory, BowlFullName));
        for (var frame = 0; frame < 6; frame++)
            BuildPour(frame).SavePng(Path.Combine(outputDirectory, $"pour-{frame}.png"));
        BuildHand().SavePng(Path.Combine(outputDirectory, "hand.png"));
        BuildBall().SavePng(Path.Combine(outputDirectory, BallName));
    }
}
