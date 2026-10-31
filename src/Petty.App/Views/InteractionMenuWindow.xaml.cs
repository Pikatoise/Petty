using System.Windows;
using System.Windows.Interop;
using Petty.App.Desktop;
using Petty.Core.Diagnostics;
using Petty.Core.Domain;

namespace Petty.App.Views;

public enum PetInteraction { Play, Pet }

public partial class InteractionMenuWindow : Window
{
    public const double MenuWidth = 82;
    public const double MenuHeight = 36;
    private readonly IAppLog _log;
    private HwndSource? _source;
    private IntPtr _handle;

    public InteractionMenuWindow(IAppLog log)
    {
        _log = log;
        InitializeComponent();
        Left = -10000;
        Top = -10000;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        MouseLeave += (_, _) => PointerChanged?.Invoke(this, EventArgs.Empty);
        MouseMove += (_, _) => PointerChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<PetInteraction>? ActionRequested;
    public event EventHandler? PointerChanged;
    public IntPtr Handle => _handle;

    public void Attach(DesktopLayerMode mode, IntPtr petHandle)
    {
        if (mode != DesktopLayerMode.Disabled) DesktopWindowService.Attach(_handle, _log, mode);
        if (mode != DesktopLayerMode.WorkerW) PlaceAbovePet(petHandle);
    }

    public void PlaceAbovePet(IntPtr petHandle)
    {
        var previous = NativeMethods.GetWindow(petHandle, NativeMethods.GwHwndPrev);
        if (previous == _handle) return;
        NativeMethods.SetWindowPos(_handle, previous, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    public void Place(ScreenRect rect) => DesktopWindowService.PlaceAt(_handle,
        (int)Math.Round(rect.X), (int)Math.Round(rect.Y), (int)Math.Round(rect.Width), (int)Math.Round(rect.Height));

    public bool ContainsScreenPoint(int x, int y)
    {
        if (!IsVisible || !NativeMethods.GetWindowRect(_handle, out var rect)) return false;
        var localX = x - rect.Left;
        var localY = y - rect.Top;
        if (localX < 0 || localY < 0 || localX >= rect.Width || localY >= rect.Height) return false;
        var radius = Math.Min(8 * (NativeMethods.GetDpiForWindow(_handle) / 96.0), Math.Min(rect.Width, rect.Height) / 2.0);
        var dx = Math.Max(0, radius - Math.Min(localX, rect.Width - 1 - localX));
        var dy = Math.Max(0, radius - Math.Min(localY, rect.Height - 1 - localY));
        return dx * dx + dy * dy <= radius * radius;
    }

    private void OnPlay(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(this, PetInteraction.Play);

    private void OnPet(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(this, PetInteraction.Pet);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
        DesktopWindowService.MarkAsToolWindow(_handle, _log);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmMouseActivate) { handled = true; return new IntPtr(NativeMethods.MaNoActivate); }
        if (message == NativeMethods.WmNcHitTest)
        {
            var x = unchecked((short)(lParam.ToInt64() & 0xffff));
            var y = unchecked((short)((lParam.ToInt64() >> 16) & 0xffff));
            handled = true;
            return new IntPtr(ContainsScreenPoint(x, y) ? NativeMethods.HtClient : NativeMethods.HtTransparent);
        }
        return IntPtr.Zero;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
