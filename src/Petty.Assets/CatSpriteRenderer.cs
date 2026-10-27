using static Petty.Assets.Palette;

namespace Petty.Assets;

public enum EyeShape
{
    Open,
    Blink,
    Half,
    Closed,
    Happy,
}

public enum MouthShape
{
    Closed,
    Open,
}

/// <summary>One frame description. All coordinates are in 64x64 frame space.</summary>
public sealed class CatPose
{
    public double BodyX { get; set; } = 28;
    public double BodyY { get; set; } = 36.5;
    public double BodyRx { get; set; } = 10.5;
    public double BodyRy { get; set; } = 8.5;
    public double HeadX { get; set; } = 43.5;
    public double HeadY { get; set; } = 23.5;
    public double HeadR { get; set; } = 9.5;
    public double FrontLegDx { get; set; }
    public double BackLegDx { get; set; }
    public int FrontLift { get; set; }
    public int BackLift { get; set; }
    public double TailSway { get; set; }
    public double TailLift { get; set; }
    public double WholeLift { get; set; }
    public EyeShape Eyes { get; set; } = EyeShape.Open;
    public MouthShape Mouth { get; set; } = MouthShape.Closed;
    public bool EarDroop { get; set; }
    public bool EarsBack { get; set; }
    public bool Curled { get; set; }
    public bool PawOut { get; set; }
    public int ZzzPhase { get; set; }
}

public static class CatSpriteRenderer
{
    public const int FrameSize = 64;
    public const int GroundRow = 52;

    /// <summary>Bottom row of the filled paws; the outline pass adds one row below it.</summary>
    private const int GroundFill = 51;

    public static PixelCanvas Render(CatPose pose)
    {
        var canvas = new PixelCanvas(FrameSize, FrameSize);

        if (pose.Curled) DrawCurled(canvas, pose);
        else DrawStanding(canvas, pose);

        canvas.Outline(Outline);
        DrawWhiskers(canvas, pose);
        DrawZzz(canvas, pose);

        return canvas;
    }

    private static void DrawStanding(PixelCanvas canvas, CatPose pose)
    {
        var lift = pose.WholeLift;
        var bodyY = pose.BodyY - lift;
        var headY = pose.HeadY - lift;

        DrawTail(canvas, pose, lift);

        var legTop = bodyY + pose.BodyRy - 3.5;

        // Far legs sit behind the body and use the darker coat shade.
        DrawLeg(canvas, pose.BodyX - 6 + pose.BackLegDx - 2, legTop, pose.BackLift + pose.WholeLift, OrangeDark, CreamShade);
        DrawLeg(canvas, pose.BodyX + 6 + pose.FrontLegDx - 2, legTop, pose.FrontLift + pose.WholeLift, OrangeDark, CreamShade);

        canvas.FillEllipse(pose.BodyX, bodyY, pose.BodyRx, pose.BodyRy, Orange);

        // Near legs.
        DrawLeg(canvas, pose.BodyX - 6 + pose.BackLegDx, legTop, pose.BackLift + pose.WholeLift, Orange, Cream);
        if (!pose.PawOut)
            DrawLeg(canvas, pose.BodyX + 6 + pose.FrontLegDx, legTop, pose.FrontLift + pose.WholeLift, Orange, Cream);

        // White chest marking.
        canvas.FillEllipse(pose.BodyX + 5, bodyY + 2.5, 3.8, 5.2, Cream);

        canvas.FillEllipse(pose.HeadX, headY, pose.HeadR, pose.HeadR, Orange);
        DrawEars(canvas, pose, headY);
        DrawMuzzle(canvas, pose, headY);
        DrawFace(canvas, pose, headY);
        DrawCoatMarks(canvas, pose, bodyY);

        if (pose.PawOut)
        {
            DrawRaisedPaw(
                canvas,
                pose.BodyX + 2, bodyY + 5,
                pose.BodyX + 12, bodyY + 2,
                Orange, Cream);
        }
    }

    private static void DrawCurled(PixelCanvas canvas, CatPose pose)
    {
        var bodyY = pose.BodyY;
        var headY = pose.HeadY;
        var headX = pose.HeadX;
        var headR = pose.HeadR;

        canvas.FillEllipse(pose.BodyX, bodyY, pose.BodyRx, pose.BodyRy, Orange);

        // Back stripes on the curled body.
        canvas.FillRect((int)Math.Round(pose.BodyX) - 8, (int)Math.Round(bodyY) - 4, 3, 1, OrangeDark);
        canvas.FillRect((int)Math.Round(pose.BodyX) - 3, (int)Math.Round(bodyY) - 5, 3, 1, OrangeDark);
        canvas.FillRect((int)Math.Round(pose.BodyX) + 2, (int)Math.Round(bodyY) - 4, 2, 1, OrangeDark);

        canvas.FillEllipse(headX, headY, headR, headR, Orange);

        // Folded ears.
        canvas.FillTriangle(headX - 7, headY - 1, headX - 4.5, headY - 11, headX - 1, headY - 4, Orange);
        canvas.FillTriangle(headX + 0.5, headY - 5, headX + 3.5, headY - 12, headX + 6.5, headY - 4, Orange);
        canvas.FillTriangle(headX + 1.8, headY - 5.4, headX + 3.5, headY - 9.6, headX + 5.4, headY - 4.6, Pink);

        canvas.FillEllipse(headX + 3.5, headY + 3.2, 3.6, 2.6, Cream);

        // Closed eyes and nose.
        canvas.FillRect((int)Math.Round(headX) - 4, (int)Math.Round(headY) - 2, 2, 1, Outline);
        canvas.FillRect((int)Math.Round(headX) + 1, (int)Math.Round(headY) - 2, 2, 1, Outline);
        canvas.FillRect((int)Math.Round(headX) + 2, (int)Math.Round(headY) + 1, 2, 1, Pink);
    }

    private static void DrawTail(PixelCanvas canvas, CatPose pose, double lift)
    {
        var baseX = pose.BodyX - (pose.BodyRx * 0.8);
        var baseY = pose.BodyY - 1 - lift;
        var sway = pose.TailSway;

        var tipX = baseX - 11 + (sway * 2.5);
        var tipY = baseY - 13 - (pose.TailLift * 10) + (sway * 1.5);
        var ctrlX = baseX - 5 + (sway * 3);
        var ctrlY = baseY - 8 - (pose.TailLift * 4);

        var points = Bezier(baseX, baseY, ctrlX, ctrlY, tipX, tipY, 24);

        for (var i = 0; i < points.Count - 1; i++)
            canvas.StrokeLine(points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y, 4.0, Orange);

        canvas.FillEllipse(tipX, tipY, 2.1, 2.1, Cream);

        var a = points[8];
        var b = points[16];
        canvas.FillRect((int)Math.Round(a.X), (int)Math.Round(a.Y) - 1, 1, 2, OrangeDark);
        canvas.FillRect((int)Math.Round(b.X), (int)Math.Round(b.Y) - 1, 1, 2, OrangeDark);
    }

    private static void DrawLeg(PixelCanvas canvas, double x, double topY, double lift, uint legColor, uint pawColor)
    {
        var bottom = GroundFill - lift;
        var height = (int)Math.Round(bottom - topY);
        if (height < 2) height = 2;

        var left = (int)Math.Round(x);
        var top = (int)Math.Round(topY);

        canvas.FillRect(left, top, 3, height, legColor);
        canvas.FillEllipse(left + 1.5, bottom - 1, 2.6, 1.8, pawColor);
    }

    private static void DrawRaisedPaw(
        PixelCanvas canvas,
        double startX, double startY,
        double endX, double endY,
        uint legColor, uint pawColor)
    {
        // Dark rim keeps the limb readable where it overlaps the body and chest.
        canvas.StrokeLine(startX, startY, endX, endY, 5.0, Outline);
        canvas.FillEllipse(endX, endY, 3.2, 2.9, Outline);
        canvas.StrokeLine(startX, startY, endX, endY, 3.0, legColor);
        canvas.FillEllipse(endX, endY, 2.2, 1.9, pawColor);
    }

    private static void DrawEars(PixelCanvas canvas, CatPose pose, double headY)
    {
        var headX = pose.HeadX;
        var shift = pose.EarsBack ? -2.0 : 0.0;

        var droop = pose.EarDroop ? 3.0 : 0.0;
        var tipY = headY - 13 + droop;
        var innerTipY = headY - 11 + (droop * 0.8);

        // Back ear.
        canvas.FillTriangle(
            headX - 7 + shift, headY - 4,
            headX - 4.5 + shift, tipY,
            headX - 1 + shift, headY - 4.5,
            Orange);
        canvas.FillTriangle(
            headX - 5.4 + shift, headY - 4.8,
            headX - 4.4 + shift, innerTipY,
            headX - 2.4 + shift, headY - 5.0,
            Pink);

        // Front ear.
        canvas.FillTriangle(
            headX + 1.5 + shift, headY - 5,
            headX + 5 + shift, tipY,
            headX + 8 + shift, headY - 4,
            Orange);
        canvas.FillTriangle(
            headX + 3.2 + shift, headY - 5.2,
            headX + 5 + shift, innerTipY,
            headX + 6.6 + shift, headY - 4.8,
            Pink);
    }

    private static void DrawMuzzle(PixelCanvas canvas, CatPose pose, double headY)
    {
        canvas.FillEllipse(pose.HeadX + 3.7, headY + 3.7, 4.6, 3.4, Cream);
    }

    private static void DrawFace(PixelCanvas canvas, CatPose pose, double headY)
    {
        var headX = pose.HeadX;
        var eyeY = (int)Math.Round(headY) - 3;

        DrawEye(canvas, (int)Math.Round(headX) - 5, eyeY, pose.Eyes);
        DrawEye(canvas, (int)Math.Round(headX) + 1, eyeY, pose.Eyes);

        // Nose.
        canvas.FillRect((int)Math.Round(headX) + 2, (int)Math.Round(headY) + 1, 2, 1, Pink);

        var mouthX = (int)Math.Round(headX) + 2;
        var mouthY = (int)Math.Round(headY) + 2;

        if (pose.Mouth == MouthShape.Open)
        {
            canvas.FillRect(mouthX, mouthY, 2, 1, Outline);
            canvas.FillRect(mouthX, mouthY + 1, 2, 1, Pink);
            canvas.FillRect(mouthX - 1, mouthY - 1, 1, 1, Outline);
            canvas.FillRect(mouthX + 2, mouthY - 1, 1, 1, Outline);
        }
        else
        {
            canvas.FillRect(mouthX, mouthY, 1, 2, Outline);
            canvas.FillRect(mouthX - 1, mouthY + 1, 1, 1, Outline);
            canvas.FillRect(mouthX + 1, mouthY + 1, 1, 1, Outline);
        }
    }

    private static void DrawEye(PixelCanvas canvas, int x, int y, EyeShape shape)
    {
        switch (shape)
        {
            case EyeShape.Open:
                canvas.FillRect(x, y, 2, 2, Outline);
                break;
            case EyeShape.Blink:
            case EyeShape.Half:
            case EyeShape.Closed:
                canvas.FillRect(x, y + 1, 2, 1, Outline);
                break;
            case EyeShape.Happy:
                canvas.FillRect(x, y + 1, 1, 1, Outline);
                canvas.FillRect(x + 1, y, 1, 1, Outline);
                canvas.FillRect(x + 2, y + 1, 1, 1, Outline);
                break;
        }
    }

    private static void DrawCoatMarks(PixelCanvas canvas, CatPose pose, double bodyY)
    {
        var bodyX = (int)Math.Round(pose.BodyX);
        var y = (int)Math.Round(bodyY);

        canvas.FillRect(bodyX - 7, y - 7, 3, 1, OrangeDark);
        canvas.FillRect(bodyX - 2, y - 8, 3, 1, OrangeDark);
        canvas.FillRect(bodyX + 3, y - 6, 2, 1, OrangeDark);

        var headX = (int)Math.Round(pose.HeadX);
        var headY = (int)Math.Round(pose.HeadY - pose.WholeLift);
        canvas.FillRect(headX - 1, headY - 8, 2, 1, OrangeDark);
    }

    private static void DrawWhiskers(PixelCanvas canvas, CatPose pose)
    {
        if (pose.Curled) return;

        var headY = (int)Math.Round(pose.HeadY - pose.WholeLift);
        var x0 = (int)Math.Round(pose.HeadX) + 8;

        PlotLine(canvas, x0, headY + 3, x0 + 4, headY + 2, Outline);
        PlotLine(canvas, x0, headY + 4, x0 + 5, headY + 4, Outline);
        PlotLine(canvas, x0, headY + 5, x0 + 4, headY + 6, Outline);
    }

    private static void PlotLine(PixelCanvas canvas, int x0, int y0, int x1, int y1, uint color)
    {
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            canvas[x0, y0] = color;
            if (x0 == x1 && y0 == y1) break;

            var e2 = 2 * error;
            if (e2 >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private static void DrawZzz(PixelCanvas canvas, CatPose pose)
    {
        if (!pose.Curled || pose.ZzzPhase <= 0) return;

        var phase = Math.Clamp(pose.ZzzPhase, 1, 3);
        var x = 49 + phase;
        var y = 33 - (phase * 2);

        DrawTinyZ(canvas, x, y, Extra);

        if (phase >= 2) DrawTinyZ(canvas, x + 3, y - 4, Extra);
    }

    private static void DrawTinyZ(PixelCanvas canvas, int x, int y, uint color)
    {
        canvas.FillRect(x, y, 3, 1, color);
        canvas[x + 1, y + 1] = color;
        canvas.FillRect(x, y + 2, 3, 1, color);
    }

    private static List<(double X, double Y)> Bezier(
        double x0, double y0,
        double x1, double y1,
        double x2, double y2,
        int steps)
    {
        var points = new List<(double X, double Y)>(steps + 1);

        for (var i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            var inv = 1 - t;
            var x = (inv * inv * x0) + (2 * inv * t * x1) + (t * t * x2);
            var y = (inv * inv * y0) + (2 * inv * t * y1) + (t * t * y2);
            points.Add((x, y));
        }

        return points;
    }
}
