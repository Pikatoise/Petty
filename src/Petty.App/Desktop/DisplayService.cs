using Petty.Core.Domain;

namespace Petty.App.Desktop;

/// <summary>Reads the real monitor layout in physical pixels via Win32.</summary>
public sealed class DisplayService
{
    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var displays = new List<DisplayInfo>(2);

        var enumerated = NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                var info = new NativeMethods.MonitorInfoEx
                {
                    Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
                    DeviceName = string.Empty,
                };

                if (NativeMethods.GetMonitorInfo(monitor, ref info))
                {
                    displays.Add(new DisplayInfo(
                        string.IsNullOrWhiteSpace(info.DeviceName) ? $"DISPLAY{displays.Count + 1}" : info.DeviceName,
                        (info.Flags & NativeMethods.MonitorInfoPrimary) != 0,
                        ToScreenRect(info.Monitor),
                        ToScreenRect(info.Work)));
                }

                return true;
            },
            IntPtr.Zero);

        if (!enumerated)
            throw new InvalidOperationException(
                $"EnumDisplayMonitors failed with error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}.");

        if (displays.Count == 0)
            throw new InvalidOperationException("No display devices were reported by the operating system.");

        return displays
            .OrderByDescending(d => d.IsPrimary)
            .ThenBy(d => d.Bounds.X)
            .ToList();
    }

    public DisplayInfo Primary(IReadOnlyList<DisplayInfo> displays) =>
        displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];

    public DisplayInfo ForPoint(IReadOnlyList<DisplayInfo> displays, double x, double y) =>
        displays.FirstOrDefault(d => Contains(d.Bounds, x, y))
        ?? Primary(displays);

    private static ScreenRect ToScreenRect(NativeMethods.Rect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static bool Contains(ScreenRect rect, double x, double y) =>
        x >= rect.X && x < rect.Right && y >= rect.Y && y < rect.Bottom;
}
