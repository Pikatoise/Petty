using static Petty.Assets.Palette;

namespace Petty.Assets;

/// <summary>
/// Food bowl and ball. They are drawn by the app into their own windows, never baked
/// into pet frames, so the cat's animations stay reusable while the props are added,
/// removed or moved at runtime.
///
/// The empty and full bowl share the same silhouette on purpose: the window keeps one
/// alpha mask for click-through and only swaps the picture when food appears.
/// </summary>
public static class PropSpriteBuilder
{
    public const int BowlWidth = 24;
    public const int BowlHeight = 16;
    public const string BowlEmptyName = "bowl-empty.png";
    public const string BowlFullName = "bowl-full.png";

    public const int BallSize = 16;
    public const string BallName = "ball.png";

    public static PixelCanvas BuildBowl(int level)
    {
        var canvas = new PixelCanvas(BowlWidth, BowlHeight);

        canvas.FillEllipse(12, 8.5, 9.5, 6, Extra);       // rounded body
        canvas.FillRect(10, 13, 4, 1, CreamShade);        // foot shadow

        canvas.FillEllipse(12, 5, 10, 3, Cream);          // rim
        canvas.FillEllipse(12, 5, 7.5, 1.8, Outline);     // opening

        // Food occupies existing opaque pixels so the silhouette never changes.
        for (var i = 0; i < level; i++)
        {
            var x = 7 + i * 3;
            canvas[x, 5] = Orange;
            canvas[x + 1, 5] = CreamShade;
            if (i > 0) canvas[x, 4] = Orange;
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
