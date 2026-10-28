using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Petty.Core.Diagnostics;

namespace Petty.App.Tray;

/// <summary>
/// Notification-area icon. Exactly two commands (settings, exit); no double-click
/// action. The icon handle is created from the generated PNG and destroyed on dispose.
/// </summary>
public sealed class TrayService : IDisposable
{
    public const string TrayIconFileName = "tray-cat.png";

    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon? _iconImage;
    private readonly IntPtr _iconHandle;

    public TrayService(string iconPath, IAppLog log)
    {
        using var bitmap = new Bitmap(iconPath);
        _iconHandle = bitmap.GetHicon();
        _iconImage = Icon.FromHandle(_iconHandle);

        _menu = new ContextMenuStrip();
        _menu.Items.Add(new ToolStripMenuItem("Pet settings", null,
            (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new ToolStripMenuItem("Exit application", null,
            (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _icon = new NotifyIcon
        {
            Icon = _iconImage,
            Text = "Petty",
            ContextMenuStrip = _menu,
            Visible = true,
        };

        log.Info($"Tray icon shown ({Path.GetFileName(iconPath)}).");
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _iconImage?.Dispose();

        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
