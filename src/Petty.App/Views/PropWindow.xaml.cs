using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Petty.App.Desktop;
using Petty.App.Sprites;
using Petty.Core.Diagnostics;
using Petty.Core.Domain;

namespace Petty.App.Views;

/// <summary>
/// The bowl and the ball: small layered windows that sit on the desktop next to the pet.
/// They never activate and hit-test their own alpha mask, so their transparent corners
/// pass clicks to whatever is underneath. The bowl is interactive (click to refill, drag
/// to move); the ball is purely decorative and always click-through.
/// </summary>
public partial class PropWindow : Window
{
    /// <summary>Pixels the pointer must travel before a press counts as a drag, not a click.</summary>
    private const int DragThreshold = 4;

    private readonly PropImage _hitMask;
    private readonly PropImage? _fullImage;
    private readonly bool _interactive;
    private readonly IAppLog _log;
    private readonly int _physicalWidth;
    private readonly int _physicalHeight;
    private readonly int _floorPadding;
    private IntPtr _handle;
    private HwndSource? _source;
    private double _dpiScale = 1;
    private bool _pressed;
    private bool _dragging;
    private int _pressCursorX;
    private int _pressCursorY;
    private int _pressLeft;
    private int _pressTop;
    private bool _filled;

    public PropWindow(PropImage hitMask, PropImage? fullImage, bool interactive, IAppLog log)
    {
        _hitMask = hitMask ?? throw new ArgumentNullException(nameof(hitMask));
        _fullImage = fullImage;
        _interactive = interactive;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _physicalWidth = hitMask.Width * PetWindow.ScreenPixelsPerSpritePixel;
        _physicalHeight = hitMask.Height * PetWindow.ScreenPixelsPerSpritePixel;
        _floorPadding = (hitMask.Height - hitMask.LastOpaqueRow - 1) * PetWindow.ScreenPixelsPerSpritePixel;

        InitializeComponent();

        Left = -10000;
        Top = -10000;
        Width = _physicalWidth;
        Height = _physicalHeight;
        PropImageControl.Source = hitMask.Image;

        SourceInitialized += OnSourceInitialized;
        DpiChanged += OnDpiChanged;
        Closed += OnClosed;
    }

    /// <summary>An opaque pixel was clicked without dragging (the bowl reads this as "add food").</summary>
    public event EventHandler? Clicked;

    /// <summary>The user finished dragging the window; the position should be persisted.</summary>
    public event EventHandler? DragCompleted;

    public IntPtr Handle => _handle;

    public int PhysicalWidth => _physicalWidth;

    public int PhysicalHeight => _physicalHeight;

    /// <summary>True while the user holds the prop; the session must not move it then.</summary>
    public bool IsDragging => _dragging;

    public ScreenRect? DragBounds { get; set; }

    public (int Left, int Top, int Width, int Height) ScreenRect
    {
        get
        {
            var rect = DesktopWindowService.GetScreenRect(_handle);
            return (rect.Left, rect.Top, rect.Width, rect.Height);
        }
    }

    public void AttachToDesktop(DesktopLayerMode mode, IAppLog log)
    {
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("The window has not been initialised yet.");

        if (mode != DesktopLayerMode.Disabled) DesktopWindowService.Attach(_handle, log, mode);
    }

    /// <summary>Shows the "has food" picture; both bowls share one alpha mask.</summary>
    public void ShowFilled(bool filled)
    {
        if (_fullImage is null || _filled == filled) return;

        _filled = filled;
        PropImageControl.Source = filled ? _fullImage.Image : _hitMask.Image;
    }

    public void PlaceAt(double screenX, double screenTop) =>
        DesktopWindowService.PlaceAt(
            _handle,
            (int)Math.Round(screenX),
            (int)Math.Round(screenTop),
            _physicalWidth,
            _physicalHeight);

    /// <summary>
    /// Puts the prop on the floor line: the window may have transparent rows below the
    /// sprite, so those are taken out of the top instead of letting it hover.
    /// </summary>
    public void PlaceOnFloor(double screenX, double floorY) =>
        PlaceAt(screenX, floorY - _physicalHeight + _floorPadding);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
        ApplyDpiSize();
        DesktopWindowService.MarkAsToolWindow(_handle, _log);
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e) => ApplyDpiSize();

    /// <summary>
    /// The physical size is fixed; only the DIP size follows the monitor scale. WPF's own
    /// DPI is used so the properties it may re-apply later agree with the pixel placement.
    /// </summary>
    private void ApplyDpiSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _dpiScale = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1;
        Width = _physicalWidth / _dpiScale;
        Height = _physicalHeight / _dpiScale;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case (int)NativeMethods.WmNcHitTest:
                handled = true;
                return IsOverOpaqueScreenPixel(lParam)
                    ? new IntPtr(NativeMethods.HtClient)
                    : new IntPtr(NativeMethods.HtTransparent);

            case (int)NativeMethods.WmMouseActivate:
                handled = true;
                return new IntPtr(NativeMethods.MaNoActivate);

            case (int)NativeMethods.WmLButtonDown:
                if (!_interactive || !IsOpaqueAtClient(LoWord(lParam), HiWord(lParam))) break;

                if (NativeMethods.GetCursorPos(out var press))
                {
                    _pressed = true;
                    _dragging = false;
                    _pressCursorX = press.X;
                    _pressCursorY = press.Y;
                    _pressLeft = ScreenRect.Left;
                    _pressTop = ScreenRect.Top;
                    NativeMethods.SetCapture(_handle);
                    handled = true;
                }

                break;

            case (int)NativeMethods.WmMouseMove:
                if (!_pressed || !NativeMethods.GetCursorPos(out var move)) break;

                var deltaX = move.X - _pressCursorX;
                var deltaY = move.Y - _pressCursorY;
                if (!_dragging && Math.Max(Math.Abs(deltaX), Math.Abs(deltaY)) >= DragThreshold) _dragging = true;

                if (_dragging)
                {
                    var x = (double)(_pressLeft + deltaX);
                    var y = (double)(_pressTop + deltaY);
                    if (DragBounds is { } bounds)
                    {
                        x = Math.Clamp(x, bounds.X, Math.Max(bounds.X, bounds.Right - _physicalWidth));
                        y = Math.Clamp(y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - _physicalHeight));
                    }
                    PlaceAt(x, y);
                }
                handled = true;
                break;

            case (int)NativeMethods.WmLButtonUp:
                if (!_pressed) break;

                var dragged = _dragging;
                EndPress();

                if (dragged) DragCompleted?.Invoke(this, EventArgs.Empty);
                else Clicked?.Invoke(this, EventArgs.Empty);

                handled = true;
                break;

            case (int)NativeMethods.WmCaptureChanged:
                EndPress();
                break;
        }

        return IntPtr.Zero;
    }

    private void EndPress()
    {
        if (!_pressed) return;

        _pressed = false;
        _dragging = false;
        NativeMethods.ReleaseCapture();
    }

    private bool IsOverOpaqueScreenPixel(IntPtr lParam)
    {
        var point = new NativeMethods.Point { X = LoWord(lParam), Y = HiWord(lParam) };
        if (!NativeMethods.ScreenToClient(_handle, ref point)) return false;
        return IsOpaqueAtClient(point.X, point.Y);
    }

    private bool IsOpaqueAtClient(int clientX, int clientY)
    {
        if (!_interactive) return false;

        var spriteX = clientX * _hitMask.Width / Math.Max(1, _physicalWidth);
        var spriteY = clientY * _hitMask.Height / Math.Max(1, _physicalHeight);
        return _hitMask.IsOpaqueAt(spriteX, spriteY);
    }

    private static int LoWord(IntPtr value) => unchecked((short)(value.ToInt64() & 0xFFFF));

    private static int HiWord(IntPtr value) => unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));

    private void OnClosed(object? sender, EventArgs e)
    {
        EndPress();
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
