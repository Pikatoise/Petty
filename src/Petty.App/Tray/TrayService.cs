using Petty.App.Views;
using Petty.Core.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Petty.App.Tray;

/// <summary>
/// Notification-area icon. Exactly two commands (settings, exit); no double-click
/// action. The icon handle is created from the generated PNG and destroyed on dispose.
/// </summary>
public sealed class TrayService: IDisposable
{
    public const string TrayIconFileName = "tray-cat.png";

    private readonly NotifyIcon _icon;
    private TrayPopupWindow? _popup;
    private readonly Icon? _iconImage;
    private readonly IntPtr _iconHandle;
    private bool _disposed;

    public TrayService(string iconPath, IAppLog log)
    {
        using var bitmap = new Bitmap(iconPath);
        _iconHandle = bitmap.GetHicon();
        _iconImage = Icon.FromHandle(_iconHandle);

        _icon = new NotifyIcon
        {
            Icon = _iconImage,
            Text = "Petty",
            Visible = true,
        };

        _icon.MouseUp += OnMouseUp;

        log.Info($"Tray icon shown ({Path.GetFileName(iconPath)}).");
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;
#if DEBUG
    public event EventHandler? DebugRequested;
#endif

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        if (_popup is not null) { _popup.Close(); return; }
        var popup = new TrayPopupWindow();
        _popup = popup;
        popup.Closed += (_, _) => _popup = null;
        popup.SettingsRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        popup.ExitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
#if DEBUG
        popup.DebugRequested += (_, _) => DebugRequested?.Invoke(this, EventArgs.Empty);
#endif
        popup.ShowAtCursor();
    }

    // NotifyIcon itself handles TaskbarCreated and re-adds the same icon ID.
    public void ClosePopup() => _popup?.Close();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _popup?.Close();
        _icon.MouseUp -= OnMouseUp;
        _icon.Visible = false;
        _icon.Dispose();
        _iconImage?.Dispose();

        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
