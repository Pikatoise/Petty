using Petty.Assets;

namespace PixelPets;

// Original pixel clusters, not traced or extracted from the reference packs.
// Work at 32px, then expand each pixel to a 2x2 block in the 64px output.
internal sealed record Coat(string Id, bool Dog, uint Fur, uint Shade, uint Light, uint White, uint WhiteShade);

internal static class PetRenderer
{
    internal static readonly Coat[] Coats =
    [
        new("cat-ginger", false, 0xFFD99852, 0xFFAD643C, 0xFFF1BB72, 0xFFFFEBD0, 0xFFD7C4AB),
        new("cat-silver", false, 0xFF929DA6, 0xFF606C7D, 0xFFBDC5C8, 0xFFF0E9DD, 0xFFC7C3BA),
        new("dog-copper", true, 0xFFBF774A, 0xFF87503B, 0xFFE4A66A, 0xFFFFE9CB, 0xFFD0BDA4),
        new("dog-tricolor", true, 0xFF555566, 0xFF373B4E, 0xFF85808B, 0xFFF4E8D5, 0xFFBAADA0),
    ];

    private const uint Ink = 0xFF302C39;
    private const uint Pink = 0xFFDB9692;
    private const uint Eye = 0xFFD9DE91;

    // Symbols: # outline, o fur, s shade, l highlight, w white, c white shade.
    private const string CatBody = """
        ....#########....
        ..##lllllllll##..
        .#llooooooooooo#.
        #loooooooooooooo#
        #ooooosooosoooww#
        #sooossooosoooww#
        .#sssssssssswwcc#
        ..###sssss#####..
        .....#####.......
        """;

    private const string DogBody = """
        ...###########.....
        .##lllllllllll##...
        #lloooooooooooow#..
        #ooooooooooooowww#.
        #ooooosoooooowwww#.
        #sooosssooocwwwww#.
        .#sssssssccwwwwc#..
        ..###ssssccwwc##...
        .....#########.....
        """;

    private const string CatHead = """
        .#.....#...
        #o#...#o#..
        #op###po#..
        #olllloo#..
        #looooooo#.
        #oooooooow#
        #oooowwwwwn
        .#oowwwww#.
        ..#wwccc#..
        ...#####...
        """;

    private const string DogHead = """
        ...#####......
        ..#lllll##....
        .#osoooooo#...
        #osssoooooo#..
        #osssooowwww##
        #osssowwwwwwwn
        .#sssoowwwwww#
        ..#ssoowccc##.
        ...#soowww#...
        ....######....
        """;

    private const string Curled = """
        ......########..........
        ....##llllllll##........
        ...#looooooooooo#.......
        ..#ooosooosoooooo#......
        .#ooossooosooooooo#.....
        #oooossssssoooooooo#....
        #ooooss#####ooowwwww#...
        #soo##looooo##wwwwwww#..
        #ss#looooooooowwwwwwc#..
        .#s#ooolllllloowwwcc#...
        ..##ssssssssssscccc#....
        ....###############.....
        """;

    internal static PixelCanvas Render(Coat coat, string clip, int frame)
    {
        var c = new PixelCanvas(32, 32);
        if (clip == "sleep")
        {
            Stamp(c, Curled, 4, 15, coat);
            Head(c, coat, coat.Dog ? 17 : 18, 17, true);
            // The resting head stays low; breathing only changes the back highlight.
            if (frame % 4 is 1 or 2) c.FillRect(12, 15, 5, 1, coat.Light);
            return c.Upscale(2);
        }

        var moving = clip is "walk" or "run";
        var swing = new[] { -2, -1, 0, 2, 2, 1, 0, -2 }[frame % 8];
        var lift = clip == "run" ? new[] { 0, 1, 2, 2, 0, 0, 1, 1 }[frame % 8] : 0;
        var bob = moving && frame % 4 == 1 ? -1 : 0;
        var bodyY = (coat.Dog ? 14 : 15) + bob - lift;
        var headX = coat.Dog ? 17 : 20;
        var headY = (coat.Dog ? 9 : 8) + bob - lift;
        var sit = clip == "sit";
        var eat = clip == "eat";
        var stretch = clip == "stretch";
        var play = clip == "play";

        if (sit) { bodyY = 18; headX -= 2; headY = 9; }
        if (eat) { headY = 16 + (frame % 3 == 1 ? 1 : 0); headX = coat.Dog ? 16 : 20; }
        if (stretch) { headY = 16 + (frame % 4 == 1 ? 1 : 0); bodyY = 16; }
        if (play) { headY = 13 + (frame % 4 > 1 ? 2 : 0); bodyY = 15; }

        Tail(c, coat, bodyY, frame, sit);
        // Far limbs: phase opposed to the near pair, shaded for depth.
        Leg(c, coat, 12, bodyY + 5, moving ? -swing : 0, 25 - lift, true);
        Leg(c, coat, 23, bodyY + 4, moving ? swing : 0, 25 - lift, true);
        Stamp(c, coat.Dog ? DogBody : CatBody, 9, bodyY, coat);

        if (sit)
        {
            // Compact haunch and vertical chest, rather than shrinking a standing pose.
            c.FillRect(16, 17, 5, 9, coat.Fur);
            c.FillRect(19, 19, 3, 7, coat.White);
            Leg(c, coat, 13, 23, -1, 26, false);
            Leg(c, coat, 21, 19, 0, 26, false);
        }
        else
        {
            var stride = clip == "run" ? swing * 2 : swing;
            var backLift = moving && swing < 0 ? 1 : 0;
            var frontLift = moving && swing > 0 ? 1 : 0;
            Leg(c, coat, 12, bodyY + 5, moving ? stride : 0, 26 - lift - backLift, false);
            Leg(c, coat, 23, bodyY + 4, moving ? -stride : 0, 26 - lift - frontLift, false);
            if (stretch || play)
            {
                c.FillRect(22, 22, 2, 4, coat.White);
                c.FillRect(23, 26, 6, 1, Ink);
                c.FillRect(24, 25, play && frame % 4 > 1 ? 3 : 5, 1, coat.White);
                if (play && frame % 4 > 1)
                {
                    Line(c, (23, 22), (27, 22), Ink, 3);
                    Line(c, (23, 22), (27, 22), coat.White, 1);
                }
            }
        }

        Head(c, coat, headX, headY, eat || (clip is "idle" or "sit" && frame == 6));
        if (eat && frame % 2 == 1) c[coat.Dog ? 30 : 29, 26] = Pink;
        return c.Upscale(2);
    }

    private static void Head(PixelCanvas c, Coat coat, int x, int y, bool closed)
    {
        Stamp(c, coat.Dog ? DogHead : CatHead, x, y, coat);
        var ex = x + (coat.Dog ? 8 : 7);
        var ey = y + (coat.Dog ? 3 : 4);
        c[ex, ey] = Ink;
        if (!closed) { c[ex, ey - 1] = Eye; c[ex + 1, ey - 1] = Ink; }
        else c[ex + 1, ey] = Ink;
        if (!coat.Dog) { c[x + 4, y + 3] = coat.Shade; c[x + 5, y + 3] = coat.Shade; }
        // Tricolor has a tan cheek and eyebrow, beyond a palette-only recolor.
        if (coat.Id == "dog-tricolor")
        {
            c.FillRect(x + 5, y + 5, 2, 2, 0xFFCA9160);
            c[x + 7, y + 2] = 0xFFCA9160;
        }
    }

    private static void Tail(PixelCanvas c, Coat coat, int bodyY, int frame, bool sit)
    {
        var sway = new[] { 0, 0, 1, 1, 0, 0, -1, -1 }[frame % 8];
        (int X, int Y)[] points = coat.Dog
            ? [(11, bodyY + 3), (7, bodyY + 2), (4, bodyY + sway), (2, bodyY - 2 + sway)]
            : [(10, bodyY + 4), (6, bodyY + 2), (4, bodyY - 1), (4 + sway, bodyY - 5), (6 + sway, bodyY - 6)];
        if (sit) points = [(12, 24), (8, 25), (5, 25), (3, 24)];
        for (var i = 1; i < points.Length; i++)
            Line(c, points[i - 1], points[i], Ink, 3);
        for (var i = 1; i < points.Length; i++)
            Line(c, points[i - 1], points[i], coat.Fur, 1);
        var tip = points[^1];
        c[tip.X, tip.Y] = coat.White;
    }

    private static void Leg(PixelCanvas c, Coat coat, int x, int y, int swing, int floor, bool far)
    {
        var knee = (X: x + swing / 2, Y: y + 2);
        var foot = (X: Math.Clamp(x + swing, 2, 28), Y: floor - 1);
        Line(c, (x, y), knee, Ink, 3);
        Line(c, knee, foot, Ink, 3);
        Line(c, (x, y), knee, far ? coat.Shade : coat.Fur, 1);
        Line(c, knee, foot, far ? coat.WhiteShade : coat.White, 1);
        c.FillRect(foot.X, floor - 1, 2, 1, far ? coat.WhiteShade : coat.White);
        c.FillRect(foot.X, floor, 3, 1, Ink);
    }

    private static void Line(PixelCanvas c, (int X, int Y) a, (int X, int Y) b, uint color, int width)
    {
        var steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        for (var i = 0; i <= steps; i++)
        {
            var t = steps == 0 ? 0 : (double)i / steps;
            var x = (int)Math.Round(a.X + (b.X - a.X) * t);
            var y = (int)Math.Round(a.Y + (b.Y - a.Y) * t);
            c.FillRect(x - width / 2, y - width / 2, width, width, color);
        }
    }

    private static void Stamp(PixelCanvas c, string template, int x, int y, Coat coat)
    {
        var rows = template.Split('\n');
        for (var row = 0; row < rows.Length; row++)
            for (var col = 0; col < rows[row].Length; col++)
            {
                var color = rows[row][col] switch
                {
                    '#' or 'n' => Ink, 'o' => coat.Fur, 's' => coat.Shade,
                    'l' => coat.Light, 'w' => coat.White, 'c' => coat.WhiteShade,
                    'p' => Pink, _ => 0u,
                };
                if (color == 0) continue;
                // Fail instead of silently clipping the character.
                if (!c.InBounds(x + col, y + row)) throw new InvalidOperationException("Clipped template.");
                c[x + col, y + row] = color;
            }
    }
}
