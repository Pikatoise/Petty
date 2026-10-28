using System.Runtime.InteropServices;
using System.Text;
using Petty.Core.Diagnostics;

namespace Petty.App.Desktop;

public enum DesktopLayerMode
{
    /// <summary>Child of the shell's wallpaper window: drawn on the desktop, behind icons and normal windows.</summary>
    WorkerW,

    /// <summary>
    /// The shell exposes no usable wallpaper window, so the pet stays a normal
    /// (non-topmost) window sitting directly above the desktop in the z-order.
    /// It is still covered by every other window, but it is drawn above desktop icons.
    /// </summary>
    AboveDesktop,

    /// <summary>Desktop integration was skipped on purpose (diagnostics only).</summary>
    Disabled,
}

public sealed record DesktopAttachment(
    DesktopLayerMode Mode,
    long HostHandle,
    string HostClass,
    string Detail)
{
    public static DesktopAttachment Disabled() =>
        new(DesktopLayerMode.Disabled, 0, string.Empty, "Desktop integration disabled by the command line.");
}

public sealed record WindowDiagnostics(
    long Handle,
    long Parent,
    string ParentClass,
    long AncestorParent,
    long Root,
    string ClassName,
    int Left,
    int Top,
    int Width,
    int Height,
    string Style,
    string ExStyle,
    bool Topmost,
    bool NoActivate,
    bool ToolWindow,
    bool Layered,
    bool Visible);

/// <summary>One desktop-host candidate, recorded so the shell structure can be audited after a run.</summary>
public sealed record HostCandidate(
    string Class,
    long Handle,
    string Rect,
    bool Visible,
    string ExStyle,
    bool Accepted,
    string Note);

public sealed record HitTestSample(int ScreenX, int ScreenY, int Result, string Meaning);

/// <summary>
/// Places the pet on the Windows desktop.
///
/// The desktop is drawn by a shell window (historically a top-level <c>WorkerW</c>,
/// on current Windows 11 a <c>WorkerW</c> that is a child of <c>Progman</c>).
/// Reparenting the pet window into it makes the pet part of the wallpaper layer:
/// visible when the desktop is exposed, covered by ordinary windows, and never
/// placed above them with HWND_TOPMOST.
///
/// When no usable host exists the pet is positioned directly above <c>Progman</c>
/// instead. That fallback is still non-topmost, but it is painted over desktop
/// icons; this is reported as a documented limitation.
///
/// All shell knowledge is isolated here so the rest of the application never
/// depends on the desktop window hierarchy.
/// </summary>
public static class DesktopWindowService
{
    private const int MaxClassName = 256;

    /// <summary>A host must cover at least this share of the virtual screen to be trusted.</summary>
    private const double MinCoverage = 0.5;

    public static IReadOnlyList<HostCandidate> LastCandidates { get; private set; } = Array.Empty<HostCandidate>();

    /// <summary>
    /// Audits the window styles. The styles themselves are owned by WPF (window
    /// properties plus the WM_MOUSEACTIVATE hook in <c>PetWindow</c>); touching them
    /// with SetWindowLongPtr breaks WPF's layered composition, so this only reports.
    /// </summary>
    public static void VerifyPetWindowStyles(IntPtr hwnd, IAppLog log)
    {
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        var layered = (exStyle & NativeMethods.WsExLayered) != 0;
        var toolWindow = (exStyle & NativeMethods.WsExToolWindow) != 0;
        var noActivate = (exStyle & NativeMethods.WsExNoActivate) != 0;
        var owner = NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner);

        if (!layered)
        {
            log.Warn(
                $"The pet window is not layered (ex-style 0x{exStyle:X}); " +
                "per-pixel transparency and click-through will not work.");
            return;
        }

        // ShowInTaskbar="False" is implemented by WPF with a hidden owner window, which
        // keeps the pet out of the taskbar; activation is refused in WM_MOUSEACTIVATE.
        log.Info(
            $"Window styles: ex-style 0x{exStyle:X} (layered, tool window={toolWindow}, " +
            $"no-activate bit={noActivate}); owner 0x{owner.ToInt64():X} hides the taskbar button.");
    }

    public static DesktopAttachment Attach(IntPtr hwnd, IAppLog log, DesktopLayerMode requested = DesktopLayerMode.AboveDesktop)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (hwnd == IntPtr.Zero)
            throw new ArgumentException("A valid window handle is required.", nameof(hwnd));

        var progman = NativeMethods.FindWindow("Progman", null);
        log.Info($"Desktop integration: Progman 0x{progman.ToInt64():X}; requested mode {requested}.");

        if (requested == DesktopLayerMode.WorkerW)
        {
            var attachment = TryAttachToWallpaper(hwnd, progman, log);
            if (attachment is not null) return attachment;

            log.Warn("Desktop integration: falling back to the above-desktop placement.");
        }

        // Default path. Reparenting a WPF layered window into the shell's wallpaper
        // window makes it a child window that this shell does not composite (the pet
        // is then invisible but still exists), so the pet normally stays top-level and
        // is placed immediately above the desktop in the z-order. That keeps it
        // visible on an exposed desktop and below every ordinary window.
        var aboveDesktop = progman == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.GetWindow(progman, NativeMethods.GwHwndPrev);

        var placed = NativeMethods.SetWindowPos(
            hwnd,
            aboveDesktop,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);

        log.Info(
            $"Desktop integration: above-desktop placement (SetWindowPos ok={placed}, " +
            $"insert after 0x{aboveDesktop.ToInt64():X}); the pet is non-topmost but is painted over desktop icons.");

        return new DesktopAttachment(
            DesktopLayerMode.AboveDesktop,
            0,
            string.Empty,
            "Non-topmost window placed just above the desktop; covered by normal windows but drawn above desktop icons.");
    }

    /// <summary>Reparents the pet into the wallpaper window. Returns null when that is not possible.</summary>
    private static DesktopAttachment? TryAttachToWallpaper(IntPtr hwnd, IntPtr progman, IAppLog log)
    {
        var host = FindWallpaperHost(progman, log);

        if (host == IntPtr.Zero)
        {
            log.Warn("Desktop integration: no usable wallpaper window found.");
            return null;
        }

        var previous = ShellParent(hwnd);

        Marshal.SetLastPInvokeError(0);
        NativeMethods.SetParent(hwnd, host);
        var error = Marshal.GetLastPInvokeError();
        var parent = ShellParent(hwnd);

        // GetParent() returns the *owner* for popup-style windows and therefore
        // reports 0 even after a successful reparent, so the ancestor chain is used
        // to decide whether the window really moved into the shell window.
        if (parent == host)
        {
            var className = ClassName(host);
            log.Info(
                $"Desktop integration: pet 0x{hwnd.ToInt64():X} reparented from 0x{previous.ToInt64():X} " +
                $"to {className} 0x{host.ToInt64():X}.");

            return new DesktopAttachment(
                DesktopLayerMode.WorkerW,
                host.ToInt64(),
                className,
                "Child of the shell wallpaper window; drawn on the desktop behind icons and normal windows.");
        }

        log.Warn(
            $"Desktop integration: SetParent to {ClassName(host)} 0x{host.ToInt64():X} did not stick " +
            $"(error {error}, parent is 0x{parent.ToInt64():X}).");

        return null;
    }

    /// <summary>
    /// Positions the window using screen coordinates. When the window is a child of
    /// the wallpaper window the coordinates are translated into the parent's client
    /// space, which keeps monitor offsets correct (including monitors left of or
    /// above the primary monitor).
    /// </summary>
    public static void PlaceAt(IntPtr hwnd, int screenX, int screenY, int width, int height)
    {
        var point = new NativeMethods.Point { X = screenX, Y = screenY };
        var parent = ShellParent(hwnd);

        if (parent != IntPtr.Zero)
            NativeMethods.MapWindowPoints(IntPtr.Zero, parent, ref point, 1);

        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            point.X,
            point.Y,
            width,
            height,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    /// <summary>
    /// Parent window when the pet has been reparented into the shell; <see cref="IntPtr.Zero"/>
    /// for a normal top-level window. <c>GetParent</c> alone is not enough because it
    /// returns the owner for popup-style windows.
    /// </summary>
    private static IntPtr ShellParent(IntPtr hwnd)
    {
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        if (root == hwnd) return IntPtr.Zero;

        return NativeMethods.GetAncestor(hwnd, NativeMethods.GaParent);
    }

    /// <summary>Screen rectangle of the window, even when it is parented to a shell window.</summary>
    internal static NativeMethods.Rect GetScreenRect(IntPtr hwnd)
    {
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return rect;
    }

    /// <summary>
    /// Sends WM_NCHITTEST for screen points to the live window. Used by the
    /// diagnostics report: it exercises the same code path a real click would.
    /// </summary>
    public static IReadOnlyList<HitTestSample> ProbeHitTest(
        IntPtr hwnd,
        IEnumerable<(int X, int Y)> screenPoints)
    {
        var samples = new List<HitTestSample>();

        foreach (var (x, y) in screenPoints)
        {
            var lParam = new IntPtr(unchecked((y << 16) | (x & 0xFFFF)));
            var result = NativeMethods.SendMessage(hwnd, NativeMethods.WmNcHitTest, IntPtr.Zero, lParam).ToInt64();
            var meaning = result switch
            {
                NativeMethods.HtTransparent => "transparent (click-through)",
                NativeMethods.HtClient => "client (interactive)",
                _ => "other",
            };

            samples.Add(new HitTestSample(x, y, (int)result, meaning));
        }

        return samples;
    }

    public static WindowDiagnostics Describe(IntPtr hwnd)
    {
        var parent = NativeMethods.GetParent(hwnd);
        var ancestor = NativeMethods.GetAncestor(hwnd, NativeMethods.GaParent);
        var rect = GetScreenRect(hwnd);
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle).ToInt64();
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();

        return new WindowDiagnostics(
            hwnd.ToInt64(),
            parent.ToInt64(),
            parent == IntPtr.Zero ? string.Empty : ClassName(parent),
            ancestor.ToInt64(),
            NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot).ToInt64(),
            ClassName(hwnd),
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height,
            $"0x{style:X}",
            $"0x{exStyle:X}",
            (exStyle & NativeMethods.WsExTopmost) != 0,
            (exStyle & NativeMethods.WsExNoActivate) != 0,
            (exStyle & NativeMethods.WsExToolWindow) != 0,
            (exStyle & NativeMethods.WsExLayered) != 0,
            NativeMethods.IsWindowVisible(hwnd));
    }

    public static string ClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return string.Empty;

        var buffer = new StringBuilder(MaxClassName);
        var length = NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }

    /// <summary>
    /// Finds the window that hosts the wallpaper. Candidates are tried in order of
    /// preference and each one is validated, because the shell also creates small
    /// helper windows with the same class name.
    /// </summary>
    private static IntPtr FindWallpaperHost(IntPtr progman, IAppLog log)
    {
        if (progman != IntPtr.Zero)
        {
            // Asks the shell to create the dedicated wallpaper window. Harmless when
            // one already exists; the shell decides whether to honour it.
            NativeMethods.SendMessageTimeout(
                progman,
                NativeMethods.SpawnWallpaperWorkerMessage,
                new IntPtr(0xD),
                IntPtr.Zero,
                NativeMethods.SmtoNormal | NativeMethods.SmtoAbortIfHung,
                1000,
                out _);

            NativeMethods.SendMessageTimeout(
                progman,
                NativeMethods.SpawnWallpaperWorkerMessage,
                new IntPtr(0xD),
                new IntPtr(1),
                NativeMethods.SmtoNormal | NativeMethods.SmtoAbortIfHung,
                1000,
                out _);
        }

        var defViewHost = IntPtr.Zero;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var defView = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero) return true;

            defViewHost = hwnd;
            log.Info(
                $"Desktop integration: SHELLDLL_DefView 0x{defView.ToInt64():X} lives in {ClassName(hwnd)} 0x{hwnd.ToInt64():X}.");
            return false;
        }, IntPtr.Zero);

        var candidates = new List<(IntPtr Handle, string Source)>();

        // 1. Current Windows 11: the wallpaper window is a child of Progman.
        var progmanChild = progman == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);

        if (progmanChild != IntPtr.Zero) candidates.Add((progmanChild, "Progman child"));

        // 2. Classic layout: a top-level WorkerW follows the window that owns the icons.
        if (defViewHost != IntPtr.Zero)
        {
            var sibling = NativeMethods.FindWindowEx(IntPtr.Zero, defViewHost, "WorkerW", null);
            if (sibling != IntPtr.Zero) candidates.Add((sibling, "sibling of SHELLDLL_DefView host"));
        }

        // 3. Any top-level WorkerW that actually covers the screen.
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!ClassName(hwnd).Equals("WorkerW", StringComparison.OrdinalIgnoreCase)) return true;

            if (candidates.All(c => c.Handle != hwnd))
                candidates.Add((hwnd, "top-level WorkerW"));

            return true;
        }, IntPtr.Zero);

        var recorded = new List<HostCandidate>(candidates.Count);
        var chosen = IntPtr.Zero;

        foreach (var (handle, source) in candidates)
        {
            NativeMethods.GetWindowRect(handle, out var rect);
            var className = ClassName(handle);
            var visible = NativeMethods.IsWindowVisible(handle);
            var exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
            var coversScreen = CoversScreen(rect);
            var isParent = NativeMethods.GetParent(handle) == progman;

            // The wallpaper host is either a Progman child that covers the screen or a
            // large top-level window; everything else is a shell helper window.
            var accepted = chosen == IntPtr.Zero && coversScreen && (visible || isParent);

            recorded.Add(new HostCandidate(
                className,
                handle.ToInt64(),
                $"{rect.Left},{rect.Top} {rect.Width}x{rect.Height}",
                visible,
                $"0x{exStyle:X}",
                accepted,
                accepted ? $"{source}; accepted" :
                !coversScreen ? $"{source}; rejected (does not cover the screen)" :
                $"{source}; rejected (not visible)"));

            if (accepted) chosen = handle;
        }

        LastCandidates = recorded;

        foreach (var candidate in recorded)
        {
            log.Info(
                $"Desktop candidate {candidate.Class} 0x{candidate.Handle:X} rect={candidate.Rect} " +
                $"visible={candidate.Visible} ex={candidate.ExStyle}: {candidate.Note}.");
        }

        return chosen;
    }

    private static bool CoversScreen(NativeMethods.Rect rect)
    {
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);

        if (width <= 0 || height <= 0) return rect.Width > 0 && rect.Height > 0;

        return rect.Width >= width * MinCoverage && rect.Height >= height * MinCoverage;
    }
}
