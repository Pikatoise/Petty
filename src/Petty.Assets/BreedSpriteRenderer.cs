namespace Petty.Assets;

/// <summary>Breed-specific pixel-cluster anatomy, coat markings and integer animation poses.</summary>
public static class BreedSpriteRenderer
{
    public const int GroundRow = 53;
    private const uint Ink = 0xFF302D3C;
    private const uint Pink = 0xFFDB9999;

    public static PixelCanvas Render(BreedDesign b, CoatDesign coat, string clip, int frame)
    {
        var c = new PixelCanvas(32, 32);
        uint[] p = coat.Palette;
        int phase = frame % 8;
        int stride = new[] { -2, -1, 1, 2, 2, 1, -1, -2 }[phase];
        bool moving = clip is "walk" or "run" or "exit";
        bool run = clip == "run";
        bool sleep = clip == "sleep";
        bool play = clip == "play";
        bool pet = clip == "pet";
        bool tired = clip == "tired";
        int bob = moving && phase % 4 == 2 ? -1 : 0;
        int lift = run ? new[] { 0, 1, 2, 2, 1, 0, 0, 0 }[phase] : 0;
        int bodyY = b.BodyY + bob - lift;
        int hx = b.HeadX;
        int hy = b.HeadY + bob - lift;
        if (clip == "eat")
        {
            hy = 17 + frame % 2;
            hx = b.Species == "dog" ? 18 : 20;
        }
        if (tired)
        {
            hy += 2;
        }

        if (pet)
        {
            hy += frame % 3 == 1 ? 1 : 0;
        }

        if (play)
        {
            hy = b.HeadY + (frame % 6 < 3 ? 4 : 1);
        }

        hy = Math.Max(b.Ears == "tuft" ? 7 : b.Ears == "large" ? 6 : 4, hy);
        if (sleep)
        {
            int width = Math.Min(23, b.Width + (b.Species == "dog" ? 1 : 3));
            int breath = frame % 4 is 1 or 2 ? 1 : 0;
            Body(c, b, coat, p, 6, 19 - (b.Fluffy ? 1 : 0) - breath, width, (b.Fluffy ? 8 : 7) + breath, false);
            hx = b.Species == "dog" ? 17 : 19;
            hy = 18;
            Tail(c, b, coat, p, 21, frame, curled: true);
        }
        else
        {
            Tail(c, b, coat, p, bodyY, frame, curled: false);
            int back = b.BodyX + 3;
            int front = b.BodyX + b.Width - 3;
            // Far-side contact pairs precede the torso; their phase opposes near legs.
            Leg(c, coat, p, back - 1, bodyY + b.Height - 2, moving ? -stride : 0, 25 - lift, true);
            Leg(c, coat, p, front - 1, bodyY + b.Height - 2, moving ? stride : 0, 25 - lift, true);
            Body(c, b, coat, p, b.BodyX, bodyY, b.Width, b.Height, clip == "idle" && frame % 4 == 1);
            int reach = run ? stride * 2 : stride;
            Leg(c, coat, p, back, bodyY + b.Height - 2, moving ? reach : 0, 25 - lift - (moving && stride < 0 ? 1 : 0), false);
            if (play && frame % 6 >= 3)
            {
                int raised = Math.Max(20, bodyY + b.Height - 1);
                Line(c, front, bodyY + b.Height - 2, 28, raised, p[0], 2);
                c.FillRect(27, raised, 2, 2, p[3]);
            }
            else
            {
                Leg(c, coat, p, front, bodyY + b.Height - 2, moving ? -reach : 0, 25 - lift - (moving && stride > 0 ? 1 : 0), false);
            }
        }
        Head(c, b, coat, p, hx, hy, sleep || tired);
        c.Outline(Ink);
        Face(c, b, coat, hx, hy, sleep || tired || pet || clip == "eat" || frame == 6);
        if (clip == "eat" && frame % 2 == 1)
        {
            c[hx + 8, hy + 7] = Pink;
        }

        return c.Upscale(2);
    }

    private static void Body(PixelCanvas c, BreedDesign b, CoatDesign coat, uint[] p, int x0, int y0, int width, int height, bool breathe)
    {
        // Deliberate stepped row profiles; no ellipse rasterization or resampling.
        for (int y = 0; y < height; y++)
        {
            int inset = y == 0 ? 3 : y == 1 || y == height - 1 ? 1 : 0;
            for (int x = inset; x < width - inset; x++)
            {
                int shade = y >= height - 2 ? 1 : y < 2 ? 2 : 0;
                if (breathe && y == 1)
                {
                    shade = 0;
                }

                c[x0 + x, y0 + y] = Color(coat, p, "body", x, y, width, height, shade);
            }
        }
        if (b.Fluffy)
        {
            c[x0, y0 + 2] = p[2];
            c[x0 + width - 1, y0 + 1] = p[2];
            c[x0 + width - 2, y0 + height - 1] = Color(coat, p, "body", width - 2, height - 1, width, height, 1);
        }
    }

    private static void Head(PixelCanvas c, BreedDesign b, CoatDesign coat, uint[] p, int x0, int y0, bool rest)
    {
        string[] rows = b.Head switch
        {
            "muzzle" => new[] { "..ooooo....", ".olloooo...", "loooooooo..", "ooooooowwo.", "ooooowwwwww", "osooowwwwww", ".sssowwww..", "..ssccc...." },
            "wedge" => new[] { "..oooo...", ".lloooo..", "looooooo.", "oooooooow", "oooooowww", ".oooowwww", "..soowww.", "...sccc.." },
            "flat" => new[] { ".oooooo..", "lllooooo.", "loooooooo", "oooooooow", "oooooowww", "ooooowwww", ".ssswwwc.", "..scccc.." },
            _ => new[] { "..ooooo..", ".llooool.", "llooooooo", "loooooooo", "oooooooow", "oooooowww", ".oooowww.", "..ssccc.." },
        };
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < rows[y].Length; x++)
            {
                char symbol = rows[y][x];
                if (symbol == '.')
                {
                    continue;
                }

                int shade = symbol is 's' or 'c' ? 1 : symbol == 'l' ? 2 : 0;
                uint color = Color(coat, p, "head", x, y, 10, 8, shade);
                if (symbol is 'w' or 'c' && HasWhite(coat.Pattern))
                {
                    color = p[symbol == 'c' ? 4 : 3];
                }

                c[x0 + x, y0 + y] = color;
            }
        }

        if (rest)
        {
            c.FillRect(x0 + 1, y0, 2, 2, p[0]);
            return;
        }
        int earHeight = b.Ears is "large" or "tuft" ? 4 : 2;
        uint earColor = coat.Pattern is "point" or "lynx" ? p[5] : p[0];
        if (b.Ears == "drop")
        {
            for (int y = 0; y < 7; y++)
            {
                c.FillRect(x0, y0 + y, y == 6 ? 2 : 3, 1, p[1]);
            }
        }
        else
        {
            for (int y = 0; y < earHeight; y++)
            {
                c.FillRect(x0 + 1, y0 - earHeight + y, 1 + y / 2, 1, earColor);
                c.FillRect(x0 + 6, y0 - earHeight + y, 1 + y / 2, 1, earColor);
            }
            c[x0 + 2, y0 - 1] = Pink;
            c[x0 + 7, y0 - 1] = Pink;
            if (b.Ears == "semi")
            {
                c.FillRect(x0, y0 - 2, 2, 2, p[1]);
            }

            if (b.Ears == "tuft")
            {
                c[x0 + 6, y0 - 5] = p[1];
            }
        }
    }

    private static void Face(PixelCanvas c, BreedDesign b, CoatDesign coat, int x, int y, bool closed)
    {
        int ex = x + 6;
        int ey = y + 3;
        c[ex, ey] = Ink;
        if (closed)
        {
            c[ex + 1, ey] = Ink;
        }
        else
        {
            c[ex, ey - 1] = b.Id is "siamese" or "siberian-husky" ? 0xFF9DCDE0 : 0xFFD4D68D;
            c[ex + 1, ey - 1] = Ink;
        }
        c[x + (b.Species == "dog" ? 10 : 8), y + 4] = b.Species == "dog" ? Ink : Pink;
        c[x + 7, y + 6] = Ink;
    }

    private static bool HasWhite(string pattern) => pattern is "bicolor" or "tabby" or "classic" or "spotted" or "merle" or "brindle" or "tricolor" or "mask" or "agouti" or "piebald" or "shaded";

    private static uint Color(CoatDesign coat, uint[] p, string part, int x, int y, int width, int height, int shade)
    {
        string pattern = coat.Pattern;
        bool white = HasWhite(pattern) && (part == "paw" || part == "body" && x >= width - 4 && y >= height / 3 || part == "head" && y > 4 && x > 4);
        if (pattern == "piebald")
        {
            white |= part == "body" && (x < 6 || x > 10) || part == "head" && x > 3;
        }

        if (white)
        {
            return p[shade == 1 ? 4 : 3];
        }

        bool mark = pattern switch
        {
            "tabby" => part == "body" ? (x % 5 < 2 && y < height - 2) : x is 3 or 4 && y < 3,
            "classic" => part == "body" && (Math.Abs(x - width / 2) + Math.Abs(y - height / 2) * 2) % 7 < 2,
            "spotted" => part == "body" && (x + 2 * (y / 3)) % 6 < 2 && y % 3 < 2,
            "smoke" => y < height / 2,
            "tortie" => (x / 3 + y / 3) % 3 == 0,
            "point" => part is "head" or "paw" or "tail-tip",
            "lynx" => part == "head" ? (y < 3 && x % 3 != 0 || y >= 5) : part is "paw" or "tail-tip",
            "shaded" => y < 2,
            "sable" or "agouti" => y < height / 2 && (x + y) % 3 != 0,
            "tricolor" => part == "head" && (y == 1 || x < 4 && y > 3) || part == "body" && y > height - 3 || part == "paw",
            "merle" or "dapple" => (x / 3 + y / 2 * 2) % 5 < 2,
            "brindle" => (x + y / 2) % 5 < 2,
            "mask" => part == "head" && x < 5 || part == "body" && y < 3,
            _ => false,
        };
        return mark ? p[5] : p[shade];
    }

    private static void Tail(PixelCanvas c, BreedDesign b, CoatDesign coat, uint[] p, int bodyY, int frame, bool curled)
    {
        int sway = new[] { 0, 0, 1, 1, 0, 0, -1, -1 }[frame % 8];
        int baseX = b.BodyX + 2;
        (int X, int Y)[] points = curled ? [(8, 23), (7, 24), (13, 24), (17, 24)] : b.Tail switch
        {
            "upright" => [(baseX, bodyY + 4), (5, bodyY + 2), (4, bodyY - 2), (4 + sway, bodyY - 6), (6 + sway, bodyY - 7)],
            "plume" => [(baseX, bodyY + 4), (6, bodyY + 3), (3, bodyY + 1 + sway), (3, bodyY - 2 + sway)],
            "curl" => [(baseX, bodyY + 4), (5, bodyY + 1), (4, bodyY - 3), (7, bodyY - 5 + sway), (9, bodyY - 3 + sway)],
            _ => [(baseX, bodyY + 3), (6, bodyY + 2), (3, bodyY + sway)],
        };
        int thickness = b.Tail is "plume" or "curl" ? 3 : 1;
        for (int i = 1; i < points.Length; i++)
        {
            Line(c, points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y, coat.Pattern is "point" or "lynx" ? p[5] : p[0], thickness);
        }

        (int X, int Y) tip = points[^1];
        c[tip.X, tip.Y] = Color(coat, p, "tail-tip", 0, 0, 1, 1, 2);
    }

    private static void Leg(PixelCanvas c, CoatDesign coat, uint[] p, int x, int y, int swing, int floor, bool far)
    {
        int foot = Math.Clamp(x + swing, 4, 28);
        int kneeY = Math.Min(floor - 1, y + 2);
        int kneeX = x + swing / 2;
        Line(c, x, y, kneeX, kneeY, p[far ? 1 : 0], 2);
        Line(c, kneeX, kneeY, foot, floor - 1, coat.Pattern is "point" or "lynx" ? p[5] : p[far ? 1 : 0], 1);
        c.FillRect(foot, floor - 1, 2, 1, Color(coat, p, "paw", 0, 1, 2, 2, far ? 1 : 0));
        c.FillRect(foot, floor, 2, 1, Ink);
    }

    private static void Line(PixelCanvas c, int ax, int ay, int bx, int by, uint color, int width)
    {
        int steps = Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay));
        for (int i = 0; i <= steps; i++)
        {
            double t = steps == 0 ? 0 : (double)i / steps;
            int x = (int)Math.Round(ax + (bx - ax) * t);
            int y = (int)Math.Round(ay + (by - ay) * t);
            if (x - width / 2 < 1 || x + width / 2 > 29 || y - width / 2 < 1 || y + width / 2 > 25)
            {
                throw new InvalidDataException($"Limb/tail exceeds safe canvas: {x},{y}.");
            }

            c.FillRect(x - width / 2, y - width / 2, width, width, color);
        }
    }
}
