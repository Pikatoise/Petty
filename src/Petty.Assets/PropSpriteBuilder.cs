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

    public static PixelCanvas BuildBowl(bool full)
    {
        var canvas = new PixelCanvas(BowlWidth, BowlHeight);

        canvas.FillEllipse(12, 8.5, 9.5, 6, Extra);       // rounded body
        canvas.FillRect(10, 13, 4, 1, CreamShade);        // foot shadow

        canvas.FillEllipse(12, 5, 10, 3, Cream);          // rim
        canvas.FillEllipse(12, 5, 7.5, 1.8, Outline);     // opening

        if (full)
        {
            canvas.FillEllipse(12, 5, 6.5, 1.3, Orange);  // kibble
            canvas[8, 5] = Cream;
            canvas[13, 4] = Cream;
            canvas[16, 5] = Cream;
        }

        canvas.Outline(Outline);
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

    public static void Write(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        BuildBowl(full: false).SavePng(Path.Combine(outputDirectory, BowlEmptyName));
        BuildBowl(full: true).SavePng(Path.Combine(outputDirectory, BowlFullName));
        BuildBall().SavePng(Path.Combine(outputDirectory, BallName));
    }
}
