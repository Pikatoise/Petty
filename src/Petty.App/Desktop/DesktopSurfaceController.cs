using Petty.Core.Diagnostics;
using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.App.Desktop;

internal enum SurfaceChange { None, Range, Display }

/// <summary>Tracks the primary display, taskbar and eased walking floor.</summary>
internal sealed class DesktopSurfaceController
{
    private const int DisplayReloadTickCount = 16;
    private readonly DisplayService _displays;
    private readonly IAppLog _log;
    private readonly int _windowWidth;
    private readonly ShellBarProbe _shellBars = new();
    private readonly FloorFollow _floor = new();
    private IReadOnlyList<DisplayInfo> _layout = Array.Empty<DisplayInfo>();
    private int _reloadTicks = DisplayReloadTickCount;

    public DesktopSurfaceController(DisplayService displays, int windowWidth, IAppLog log)
    {
        _displays = displays;
        _windowWidth = windowWidth;
        _log = log;
    }

    public MotionArea WalkArea { get; private set; }
    public double FloorY => _floor.Current;
    public bool IsFloorMoving => WalkArea.FloorY > 0 && !_floor.IsAt(WalkArea.FloorY);
    public DisplayInfo? PrimaryDisplay { get; private set; }
    public IReadOnlyList<DisplayInfo> Displays => _layout;
    public ShellBarSnapshot? ShellBar { get; private set; }

    public void Load()
    {
        _layout = _displays.GetDisplays();
        PrimaryDisplay = _displays.Primary(_layout);
        Refresh(force: true);
    }

    public SurfaceChange Poll()
    {
        if (--_reloadTicks <= 0)
        {
            _reloadTicks = DisplayReloadTickCount;
            try
            {
                var displays = _displays.GetDisplays();
                if (!_layout.SequenceEqual(displays))
                {
                    _layout = displays;
                    PrimaryDisplay = _displays.Primary(displays);
                    _log.Info("Display layout changed: " +
                              string.Join(", ", displays.Select(d => $"{d.DeviceName} {FormatRect(d.Bounds)}")) + ".");
                    Refresh(force: true);
                    return SurfaceChange.Display;
                }
            }
            catch (InvalidOperationException ex)
            {
                _log.Warn($"Could not re-read the display layout: {ex.Message}");
            }
        }
        return Refresh(force: false);
    }

    public bool AdvanceFloor(TimeSpan delta)
    {
        if (WalkArea.FloorY <= 0) return false;
        var before = _floor.Current;
        _floor.Advance(WalkArea.FloorY, delta);
        return Math.Abs(_floor.Current - before) > 0.01;
    }

    private SurfaceChange Refresh(bool force)
    {
        var display = PrimaryDisplay;
        if (display is null) return SurfaceChange.None;
        ShellBar = _shellBars.Read(display);
        var visibleBar = ShellBar is { Revealed: true } bar ? bar.Rect : (ScreenRect?)null;
        var area = WalkSurface.Compute(display, visibleBar, _windowWidth);
        if (!force && area == WalkArea) return SurfaceChange.None;

        var previous = WalkArea;
        WalkArea = area;
        if (force)
        {
            _floor.Snap(area.FloorY);
            _log.Info($"Walk area: x {area.Left:F0}..{area.Right:F0}, floor {area.FloorY:F0} " +
                      $"(display {FormatRect(display.Bounds)}, work {FormatRect(display.UsableArea)}, " +
                      $"bar {(visibleBar is { } b ? FormatRect(b) : "hidden/none")}).");
            return SurfaceChange.Display;
        }
        if (Math.Abs(area.FloorY - previous.FloorY) > 0.5)
            _log.Info(area.FloorY < previous.FloorY
                ? $"Taskbar shown: the pet steps up to floor {area.FloorY:F0}."
                : $"Taskbar hidden: the pet returns to floor {area.FloorY:F0}.");
        if (area.Left != previous.Left || area.Right != previous.Right)
        {
            _log.Info($"Walk range changed to x {area.Left:F0}..{area.Right:F0}.");
            return SurfaceChange.Range;
        }
        return SurfaceChange.None;
    }

    private static string FormatRect(ScreenRect rect) =>
        $"{rect.X:F0},{rect.Y:F0} {rect.Width:F0}x{rect.Height:F0}";
}
