using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Petty.App.Desktop;

/// <summary>Receives shell broadcasts and detects Show Desktop covering the pet's desktop layer.</summary>
internal sealed class ShellRestartWatcher : IDisposable
{
    private readonly HwndSource _source;
    private readonly uint _message = RegisterWindowMessage("TaskbarCreated");
    private readonly DispatcherTimer _desktopTimer;
    private readonly Func<IntPtr> _desktopWindow;
    private readonly Action _restore;
    private bool _disposed;
    private bool _pending;

    public ShellRestartWatcher(Action restore, Func<IntPtr> desktopWindow)
    {
        _restore = restore;
        _desktopWindow = desktopWindow;
        if (_message == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _source = new HwndSource(new HwndSourceParameters("Petty.ShellNotifications") { WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(OnMessage);
        _desktopTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _desktopTimer.Tick += OnDesktopPoll;
        _desktopTimer.Start();

        IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((uint)message == _message && !_disposed && !_pending)
            {
                _pending = true;
                _source.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    _pending = false;
                    if (!_disposed)
                    {
                        restore();
                    }
                });
            }
            return IntPtr.Zero;
        }
    }

    private void OnDesktopPoll(object? sender, EventArgs e)
        => RestoreDesktopOrder();

    internal void RestoreDesktopOrder(IntPtr desktop = default)
    {
        if (!_disposed && !_pending && DesktopWindowService.IsCoveredByDesktop(_desktopWindow(), desktop))
        {
            _restore();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _desktopTimer.Stop();
        _desktopTimer.Tick -= OnDesktopPoll;
        _source.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);
}
