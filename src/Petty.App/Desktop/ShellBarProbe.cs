using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.App.Desktop;

/// <summary>One live reading of the shell taskbar that belongs to a monitor.</summary>
public sealed record ShellBarSnapshot(
    long Handle,
    string ClassName,
    ScreenRect Rect,
    bool Revealed,
    string Note);

/// <summary>
/// Reads the Windows taskbar rectangle and whether it is currently shown.
///
/// The taskbar keeps its docked rectangle even while Windows hides it for auto-hide,
/// so geometry alone cannot tell whether the bar covers the desktop. A bar that is
/// really on screen is the window under its own pixels; a retracted one is not
/// hit-testable at all. That is what <c>WindowFromPoint</c> reports, and it is the
/// signal used here.
///
/// All shell knowledge stays in this class so the rest of the app only sees a
/// <see cref="ShellBarSnapshot"/> plus the pure <see cref="WalkSurface"/> maths.
/// </summary>
public sealed class ShellBarProbe
{
    private const string MainBarClass = "Shell_TrayWnd";
    private const string SecondaryBarClass = "Shell_SecondaryTrayWnd";

    /// <summary>Sample points across the bar; the pet only covers a small part of it.</summary>
    private static readonly double[] SampleFractions = [0.25, 0.5, 0.75];

    private readonly List<IntPtr> _handles = new();

    /// <summary>Reads the bar docked on <paramref name="display"/>, or null when there is none.</summary>
    public ShellBarSnapshot? Read(DisplayInfo display)
    {
        ArgumentNullException.ThrowIfNull(display);

        RefreshHandles();

        ShellBarSnapshot? best = null;
        var bestOverlap = 0.0;

        foreach (var handle in _handles)
        {
            if (!NativeMethods.GetWindowRect(handle, out var rect)) continue;

            var bar = new ScreenRect(rect.Left, rect.Top, rect.Width, rect.Height);
            var overlap = WalkSurface.Intersect(bar, display.Bounds);
            var area = overlap.Width * overlap.Height;

            if (area <= 0 || area <= bestOverlap) continue;

            bestOverlap = area;
            best = Describe(handle, bar);
        }

        return best;
    }

    private static ShellBarSnapshot Describe(IntPtr handle, ScreenRect rect)
    {
        var className = DesktopWindowService.ClassName(handle);

        if (!NativeMethods.IsWindowVisible(handle))
            return new ShellBarSnapshot(handle.ToInt64(), className, rect, false, "window hidden");

        foreach (var fraction in SampleFractions)
        {
            // A retracted auto-hide bar leaves a couple of pixels inside the screen, so
            // the sample row is taken from the middle of the reported rectangle.
            var x = (int)Math.Round(rect.X + (rect.Width * fraction));
            var y = (int)Math.Round(rect.Y + (rect.Height / 2));

            var hit = NativeMethods.WindowFromPoint(new NativeMethods.Point { X = x, Y = y });
            if (hit == IntPtr.Zero) continue;

            var root = NativeMethods.GetAncestor(hit, NativeMethods.GaRoot);
            if (root == handle)
                return new ShellBarSnapshot(handle.ToInt64(), className, rect, true, $"hit at {x},{y}");
        }

        return new ShellBarSnapshot(handle.ToInt64(), className, rect, false, "not hit-testable (auto-hide)");
    }

    private void RefreshHandles()
    {
        if (_handles.Count > 0 && _handles.All(NativeMethods.IsWindow)) return;

        _handles.Clear();

        var main = NativeMethods.FindWindow(MainBarClass, null);
        if (main != IntPtr.Zero) _handles.Add(main);

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (DesktopWindowService.ClassName(hwnd) == SecondaryBarClass)
            {
                // One bar per additional monitor.
                _handles.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);
    }
}
