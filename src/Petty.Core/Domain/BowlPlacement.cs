namespace Petty.Core.Domain;

public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool HasArea => Width > 0 && Height > 0;
}

/// <summary>
/// Minimal, WPF-free description of a monitor as reported by the OS.
/// </summary>
public sealed record DisplayInfo(string DeviceName, bool IsPrimary, ScreenRect Bounds, ScreenRect WorkArea)
{
    public ScreenRect UsableArea => WorkArea.HasArea ? WorkArea : Bounds;
}

public readonly record struct BowlPlacementState(double RelativeX, double BottomOffset, string? DisplayDeviceName);

public sealed record ResolvedBowlPosition(double Left, double Top, string DisplayDeviceName);

/// <summary>
/// Resolves the bowl position from monitor-relative data so it survives resolution,
/// DPI and monitor-arrangement changes. If the saved monitor no longer exists the
/// bowl falls back to the primary monitor and is clamped into its work area.
/// </summary>
public static class BowlPlacement
{
    public const double MinRelativeX = 0.01;
    public const double MaxRelativeX = 0.99;

    public static ResolvedBowlPosition Resolve(
        BowlPlacementState placement,
        IReadOnlyList<DisplayInfo> displays,
        double bowlWidth,
        double bowlHeight)
    {
        if (displays is null || displays.Count == 0)
            throw new ArgumentException("At least one display is required.", nameof(displays));

        var display = FindDisplay(placement.DisplayDeviceName, displays);
        var area = display.UsableArea;

        var width = Math.Max(0, Math.Min(bowlWidth, area.Width));
        var height = Math.Max(0, Math.Min(bowlHeight, area.Height));

        var relativeX = double.IsNaN(placement.RelativeX)
            ? 0.5
            : Math.Clamp(placement.RelativeX, MinRelativeX, MaxRelativeX);

        var travel = Math.Max(0, area.Width - width);
        var left = area.X + (relativeX * travel);
        left = Math.Clamp(left, area.X, area.X + travel);

        var bottomOffset = double.IsNaN(placement.BottomOffset) ? 0 : Math.Max(0, placement.BottomOffset);
        bottomOffset = Math.Min(bottomOffset, Math.Max(0, area.Height - height));
        var top = area.Y + Math.Max(0, area.Height - height - bottomOffset);

        return new ResolvedBowlPosition(left, top, display.DeviceName);
    }

    /// <summary>
    /// Turns an absolute window position back into monitor-relative data for persistence.
    /// </summary>
    public static BowlPlacementState Capture(
        double left,
        double top,
        double bowlWidth,
        double bowlHeight,
        IReadOnlyList<DisplayInfo> displays)
    {
        if (displays is null || displays.Count == 0)
            throw new ArgumentException("At least one display is required.", nameof(displays));

        var display = displays.FirstOrDefault(d => Contains(d.UsableArea, left, top))
                      ?? displays.FirstOrDefault(d => d.IsPrimary)
                      ?? displays[0];
        var area = display.UsableArea;

        var width = Math.Max(0, Math.Min(bowlWidth, area.Width));
        var height = Math.Max(0, Math.Min(bowlHeight, area.Height));
        var travel = Math.Max(0, area.Width - width);

        var relativeX = travel <= 0 ? 0.5 : (left - area.X) / travel;
        var maxOffset = Math.Max(0, area.Height - height);
        var bottomOffset = area.Bottom - (top + height);

        return new BowlPlacementState(
            Math.Clamp(relativeX, MinRelativeX, MaxRelativeX),
            Math.Clamp(bottomOffset, 0, maxOffset),
            display.DeviceName);
    }

    public static DisplayInfo FindDisplay(string? deviceName, IReadOnlyList<DisplayInfo> displays)
    {
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var match = displays.FirstOrDefault(d =>
                string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
    }

    private static bool Contains(ScreenRect area, double x, double y) =>
        x >= area.X && x < area.Right && y >= area.Y && y < area.Bottom;
}
