using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Petty.App.Desktop;

/// <summary>Hidden top-level HWND receives shell broadcasts, including while the pet is hidden.</summary>
internal sealed class ShellRestartWatcher : IDisposable
{
    private readonly HwndSource _source;
    private readonly uint _message = RegisterWindowMessage("TaskbarCreated");
    private bool _disposed;
    private bool _pending;

    public ShellRestartWatcher(Action restore)
    {
        if (_message == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _source = new HwndSource(new HwndSourceParameters("Petty.ShellNotifications") { WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(OnMessage);

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);
}
