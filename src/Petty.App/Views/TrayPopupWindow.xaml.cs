using System.Windows;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace Petty.App.Views;

public partial class TrayPopupWindow : Window
{
    public TrayPopupWindow()
    {
        InitializeComponent();
        AdaptiveWindowLayout.Attach(this);
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
        AdaptiveWindowLayout.ShowPopup(this, point);
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
