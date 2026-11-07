using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Petty.App.Desktop;
using Forms = System.Windows.Forms;
using Rectangle = System.Drawing.Rectangle;

namespace Petty.App.Views;

/// <summary>Keeps ordinary WPF windows readable in DIPs and inside the current work area.</summary>
internal static class AdaptiveWindowLayout
{
    private const int Edge = 8;

    public static void Attach(Window window, Func<double>? preferredWidth = null)
    {
        double initialWidth = window.Width;
        bool updating = false;
        bool closed = false;
        void Refresh(bool center = false)
        {
            if (updating || closed)
            {
                return;
            }

            updating = true;
            try
            {
                nint handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                {
                    Fit(window, new Size(SystemParameters.WorkArea.Width - Edge * 2,
                        SystemParameters.WorkArea.Height - Edge * 2), preferredWidth?.Invoke() ?? initialWidth);
                    return;
                }

                Forms.Screen screen = center ? Forms.Screen.PrimaryScreen! : Forms.Screen.FromHandle(handle);
                Rectangle work = screen.WorkingArea;
                double scale = Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96.0;
                Fit(window, new Size((work.Width - Edge * 2) / scale, (work.Height - Edge * 2) / scale), preferredWidth?.Invoke() ?? initialWidth);
                window.UpdateLayout();
                if (window.IsVisible)
                {
                    Place(window, work, center);
                }
            }
            finally
            {
                updating = false;
            }
        }

        void Changed(object? sender, EventArgs e) => Refresh();
        void DisplayChanged(object? sender, EventArgs e) => window.Dispatcher.BeginInvoke(() => Refresh());
        void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => DisplayChanged(sender, e);
        window.SourceInitialized += Changed;
        window.Loaded += (_, _) => Refresh(window.WindowStartupLocation == WindowStartupLocation.CenterScreen);
        window.LocationChanged += Changed;
        window.SizeChanged += (_, _) => Refresh();
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Refresh());
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        window.Closed += (_, _) =>
        {
            closed = true;
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            SystemEvents.UserPreferenceChanged -= PreferenceChanged;
        };
        Refresh();
    }

    // Window limits include non-client chrome; the content ScrollViewer receives the remaining area.
    internal static void Fit(Window window, Size available, double preferredWidth)
    {
        window.MaxWidth = Math.Max(1, available.Width);
        window.MaxHeight = Math.Max(1, available.Height);
        if (double.IsFinite(preferredWidth))
        {
            window.Width = Math.Min(preferredWidth, window.MaxWidth);
        }
    }

    public static void ShowPopup(Window window, System.Drawing.Point point)
    {
        Rectangle work = Forms.Screen.FromPoint(point).WorkingArea;
        nint handle = new WindowInteropHelper(window).EnsureHandle();
        // Move the hidden HWND first so WM_DPICHANGED supplies the cursor monitor's real DPI.
        NativeMethods.SetWindowPos(handle, IntPtr.Zero, work.Left + Edge, work.Top + Edge, 0, 0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
        double scale = Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96.0;
        Fit(window, new Size((work.Width - Edge * 2) / scale, (work.Height - Edge * 2) / scale), double.NaN);
        window.Show();
        window.UpdateLayout();
        Place(window, work, false, point);
    }

    private static void Place(Window window, Rectangle work, bool center, System.Drawing.Point? anchor = null)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        if (!NativeMethods.GetWindowRect(handle, out NativeMethods.Rect rect))
        {
            return;
        }

        int width = Math.Min(rect.Width, Math.Max(1, work.Width - Edge * 2));
        int height = Math.Min(rect.Height, Math.Max(1, work.Height - Edge * 2));
        int left = center ? work.Left + (work.Width - width) / 2 : anchor?.X - width ?? rect.Left;
        int top = center ? work.Top + (work.Height - height) / 2 : anchor?.Y - height ?? rect.Top;
        left = Math.Clamp(left, work.Left + Edge, Math.Max(work.Left + Edge, work.Right - Edge - width));
        top = Math.Clamp(top, work.Top + Edge, Math.Max(work.Top + Edge, work.Bottom - Edge - height));
        NativeMethods.SetWindowPos(handle, IntPtr.Zero, left, top, width, height,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }
}
