using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Petty.App.Views;

public partial class TrayPopupWindow : Window
{
    public TrayPopupWindow()
    {
        InitializeComponent();
#if DEBUG
        var debug = new System.Windows.Controls.Button { Style = (Style)FindResource("TrayMenuItem") };
        debug.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "DebugMenu");
        debug.Click += (_, _) => { Close(); DebugRequested?.Invoke(this, EventArgs.Empty); };
        ActionsPanel.Children.Add(debug);
#endif
        Deactivated += OnDeactivated;
        Closing += (_, _) => Deactivated -= OnDeactivated;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); } };
    }

    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;
#if DEBUG
    public event EventHandler? DebugRequested;
#endif

    public void ShowAtCursor()
    {
        System.Drawing.Point point = Forms.Cursor.Position;
        Rectangle work = Forms.Screen.FromPoint(point).WorkingArea;
        Left = -10000;
        Top = -10000;
        Show();
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX;
        double scaleY = dpi.DpiScaleY;
        double width = ActualWidth * scaleX;
        double height = ActualHeight * scaleY;
        Left = Math.Clamp(point.X - width, work.Left, Math.Max(work.Left, work.Right - width)) / scaleX;
        Top = Math.Clamp(point.Y - height, work.Top, Math.Max(work.Top, work.Bottom - height)) / scaleY;
        Activate();
    }

    private void OnDeactivated(object? sender, EventArgs e) => Close();

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        Close();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }
    private void OnExit(object sender, RoutedEventArgs e)
    {
        Close();
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
