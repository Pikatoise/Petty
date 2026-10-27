using Petty.Core.Domain;

namespace Petty.Core.Runtime;

/// <summary>Screen edge a shell bar (the Windows taskbar) is docked to.</summary>
public enum ShellBarEdge
{
    None,
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>
/// Computes the band of the screen the pet may walk through.
///
/// The work area alone is not enough: with "automatically hide the taskbar" enabled
/// Windows reports the whole screen as work area, so the pet would walk along the very
/// bottom edge and be covered every time the bar slides up. This calculator takes the
/// bar's <em>currently visible</em> rectangle as an additional input, so the pet can
/// step on top of the bar while it is shown and return to the screen edge when it
/// hides. A left- or right-docked bar narrows the walkable width instead.
/// </summary>
public static class WalkSurface
{
    /// <summary>
    /// Bars thinner than this many pixels are auto-hide slivers, not real obstacles.
    /// A retracted bar is usually parked a couple of pixels inside the screen edge.
    /// </summary>
    public const double MinBarThickness = 8;

    /// <summary>Tolerance (pixels) for deciding which screen edge a bar is flush with.</summary>
    private const double EdgeTolerance = 2;

    /// <summary>
    /// Returns the horizontal range and the ground row for <paramref name="display"/>.
    /// </summary>
    /// <param name="display">Monitor bounds and work area in physical pixels.</param>
    /// <param name="visibleBar">
    /// The taskbar rectangle when the bar is actually drawn on screen, or <c>null</c>
    /// when it is hidden (auto-hide retracted) or unknown.
    /// </param>
    /// <param name="windowWidth">Pet window width in pixels; the right limit keeps it fully on screen.</param>
    public static MotionArea Compute(DisplayInfo display, ScreenRect? visibleBar, double windowWidth)
    {
        ArgumentNullException.ThrowIfNull(display);

        var monitor = display.Bounds.HasArea ? display.Bounds : display.UsableArea;
        var work = display.UsableArea;

        if (!monitor.HasArea)
            return new MotionArea(0, 0, 0);

        var left = monitor.X;
        var right = monitor.Right;
        var floor = monitor.Bottom;

        // The work area trims a permanently docked bar; honour it when it is sane.
        if (work.HasArea)
        {
            left = Math.Max(left, work.X);
            right = Math.Min(right, work.Right);
            floor = Math.Min(floor, work.Bottom);
        }

        if (visibleBar is { } bar && bar.HasArea)
        {
            var clipped = Intersect(monitor, bar);

            if (clipped.HasArea)
            {
                switch (EdgeOf(monitor, clipped))
                {
                    case ShellBarEdge.Bottom:
                        floor = Math.Min(floor, clipped.Y);
                        break;

                    case ShellBarEdge.Left:
                        left = Math.Max(left, clipped.Right);
                        break;

                    case ShellBarEdge.Right:
                        right = Math.Min(right, clipped.X);
                        break;

                    // A top bar does not touch the ground, so the pet keeps walking
                    // along the bottom edge underneath it.
                    case ShellBarEdge.Top:
                    case ShellBarEdge.None:
                    default:
                        break;
                }
            }
        }

        if (right < left) right = left;

        var maxLeft = Math.Max(left, right - Math.Max(0, windowWidth));
        var walkedFloor = Math.Clamp(floor, monitor.Y, monitor.Bottom);

        return new MotionArea(left, maxLeft, walkedFloor);
    }

    /// <summary>Which monitor edge the (already clipped) bar is attached to.</summary>
    public static ShellBarEdge EdgeOf(ScreenRect monitor, ScreenRect bar)
    {
        if (!monitor.HasArea || !bar.HasArea) return ShellBarEdge.None;

        // A taskbar is a long thin strip: compare its sides to tell a horizontal bar
        // from a vertical one, then look at which edge it is flush with.
        if (bar.Width >= bar.Height)
        {
            if (bar.Height < MinBarThickness) return ShellBarEdge.None;
            if (bar.Bottom >= monitor.Bottom - EdgeTolerance) return ShellBarEdge.Bottom;
            if (bar.Y <= monitor.Y + EdgeTolerance) return ShellBarEdge.Top;
            return ShellBarEdge.None;
        }

        if (bar.Width < MinBarThickness) return ShellBarEdge.None;
        if (bar.X <= monitor.X + EdgeTolerance) return ShellBarEdge.Left;
        if (bar.Right >= monitor.Right - EdgeTolerance) return ShellBarEdge.Right;
        return ShellBarEdge.None;
    }

    public static ScreenRect Intersect(ScreenRect a, ScreenRect b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);

        return new ScreenRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
