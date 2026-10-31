using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Petty.App.Desktop;
using Petty.App.Sprites;
using Petty.Core.Diagnostics;

namespace Petty.App.Views;

/// <summary>
/// The pet's transparent, border-less window. It never activates, never appears in
/// the taskbar and hit-tests the sprite's alpha channel so only the cat itself takes
/// mouse input. Window placement is delegated to <see cref="DesktopWindowService"/>.
/// </summary>
public partial class PetWindow : Window
{
    /// <summary>Sprite pixels are drawn at 3x, i.e. one 64x64 frame becomes 192x192 screen pixels.</summary>
    public const int ScreenPixelsPerSpritePixel = 3;

    private readonly SpriteSheet _sheet;
    private readonly IAppLog _log;
    private readonly int _physicalWidth;
    private readonly int _physicalHeight;
    private IntPtr _handle;
    private HwndSource? _source;
    private SpriteFrame? _frame;
    private bool _faceLeft;
    private bool _placementLogged;
    private bool _applyingPlacement;
    private double _dpiScale = 1;

    public PetWindow(SpriteSheet sheet, IAppLog log)
    {
        _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _physicalWidth = sheet.FrameWidth * ScreenPixelsPerSpritePixel;
        _physicalHeight = sheet.FrameHeight * ScreenPixelsPerSpritePixel;

        InitializeComponent();

        // Park the window off-screen until it has been attached to the desktop layer,
        // so it cannot flash at the top-left corner during startup.
        Left = -10000;
        Top = -10000;
        Width = _physicalWidth;
        Height = _physicalHeight;

        SourceInitialized += OnSourceInitialized;
        DpiChanged += OnDpiChanged;
        LocationChanged += OnLocationChanged;
        SizeChanged += OnSizeChanged;
        Closing += (_, _) => _closing = true;
        Closed += OnClosed;

        // The window only receives mouse input over opaque cat pixels, so this is the
        // cat's own hover, not the transparent square around it.
        Root.MouseEnter += (_, _) => PointerChanged?.Invoke(this, EventArgs.Empty);
        Root.MouseMove += (_, _) => PointerChanged?.Invoke(this, EventArgs.Empty);
        Root.MouseLeave += (_, _) => PointerChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool _closing;

    /// <summary>Raised when an opaque sprite pixel is clicked; the window never activates.</summary>
    public event EventHandler? PetClicked;

    public event EventHandler? PointerChanged;

    /// <summary>Raised when DPI or window size changed and the session must re-place the pet.</summary>
    public event EventHandler? GeometryChanged;

    public IntPtr Handle => _handle;

    public int PhysicalWidth => _physicalWidth;

    public int PhysicalHeight => _physicalHeight;

    public double DpiScale => _dpiScale;

    /// <summary>Floor row used by the last <see cref="MoveTo"/> call, for diagnostics.</summary>
    public double PlacementFloorY { get; private set; }

    /// <summary>Window top that <see cref="MoveTo"/> computed for the last placement.</summary>
    public double ExpectedTop { get; private set; }

    public DesktopAttachment Attachment { get; private set; } = DesktopAttachment.Disabled();

    public void AttachToDesktop(DesktopLayerMode mode, IAppLog log)
    {
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("The window has not been initialised yet.");

        Attachment = mode == DesktopLayerMode.Disabled
            ? DesktopAttachment.Disabled()
            : DesktopWindowService.Attach(_handle, log, mode);
    }

    public void ShowFrame(SpriteFrame frame)
    {
        _frame = frame;
        SpriteImage.Source = frame.Image;
    }

    public void SetFacing(bool faceLeft)
    {
        if (_faceLeft == faceLeft) return;

        _faceLeft = faceLeft;
        FlipTransform.ScaleX = faceLeft ? -1 : 1;
    }

    /// <summary>Places the paws on <paramref name="floorY"/> and the window at <paramref name="screenX"/>.</summary>
    public void MoveTo(double screenX, double floorY)
    {
        if (_handle == IntPtr.Zero) return;

        var scale = _physicalWidth / (double)_sheet.FrameWidth;
        var top = floorY - ((_sheet.GroundRow + 1) * scale);
        var left = (int)Math.Round(screenX);
        var topPixels = (int)Math.Round(top);

        PlacementFloorY = floorY;
        ExpectedTop = topPixels;

        _applyingPlacement = true;

        try
        {
            DesktopWindowService.PlaceAt(_handle, left, topPixels, _physicalWidth, _physicalHeight);

            if (!_placementLogged)
            {
                _placementLogged = true;

                var actual = DesktopWindowService.GetScreenRect(_handle);
                _log.Info(
                    $"Placement: requested {left},{topPixels} {_physicalWidth}x{_physicalHeight}; " +
                    $"actual {actual.Left},{actual.Top} {actual.Width}x{actual.Height} " +
                    $"(floor {floorY:F0}, ground row {_sheet.GroundRow}, scale {scale:F2}).");

                if (actual.Top != topPixels)
                {
                    _log.Warn(
                        $"Placement mismatch: the window is {actual.Top - topPixels}px below the requested row; " +
                        "the paws will not rest on the display's bottom edge.");
                }
            }
        }
        finally
        {
            _applyingPlacement = false;
        }
    }

    /// <summary>Window rectangle in screen pixels, for diagnostics.</summary>
    public (int Left, int Top, int Width, int Height) PlacementRect
    {
        get
        {
            var rect = DesktopWindowService.GetScreenRect(_handle);
            return (rect.Left, rect.Top, rect.Width, rect.Height);
        }
    }

    private double PixelScale => _physicalWidth / (double)_sheet.FrameWidth;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;

        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);

        var dpi = NativeMethods.GetDpiForWindow(_handle);
        _dpiScale = dpi > 0 ? dpi / 96.0 : 1;
        ApplyDpiSize();

        DesktopWindowService.MarkAsToolWindow(_handle, _log);
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        _dpiScale = e.NewDpi.DpiScaleX > 0 ? e.NewDpi.DpiScaleX : 1;
        _log.Info($"DPI changed to {_dpiScale:F2}.");
        ApplyDpiSize();
        GeometryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Logs moves that did not come from <see cref="MoveTo"/> (e.g. WPF layout).</summary>
    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_applyingPlacement || !_placementLogged || _closing) return;

        var rect = DesktopWindowService.GetScreenRect(_handle);
        _log.Warn($"The window moved outside the placement path to {rect.Left},{rect.Top}.");
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_applyingPlacement) return;

        var rect = DesktopWindowService.GetScreenRect(_handle);
        _log.Info(
            $"Window size changed to {e.NewSize.Width:F1}x{e.NewSize.Height:F1} DIP " +
            $"({rect.Width}x{rect.Height} px at {_dpiScale:F2}).");
    }

    private void ApplyDpiSize()
    {
        // Physical size stays constant so the pet looks the same size on every monitor;
        // only the DIP size changes with the monitor's scale factor.
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

            case (int)NativeMethods.WmLButtonUp:
                if (IsOpaqueAtClient(LoWord(lParam), HiWord(lParam)))
                {
                    PetClicked?.Invoke(this, EventArgs.Empty);
                }

                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>Topmost opaque pixel in the hovered column, falling back to a nearby column.</summary>
    public bool TryGetMenuAnchor(int relativeX, out int x, out int y)
    {
        x = y = 0;
        if (_frame is null) return false;
        var rect = DesktopWindowService.GetScreenRect(_handle);
        var column = Math.Clamp((int)(relativeX / PixelScale), 0, _sheet.FrameWidth - 1);
        for (var distance = 0; distance < _sheet.FrameWidth; distance++)
            for (var side = 0; side < 2; side++)
            {
                var candidate = side == 0 ? column - distance : column + distance;
                if (candidate < 0 || candidate >= _sheet.FrameWidth) continue;
                for (var row = 0; row < _sheet.FrameHeight; row++)
                    if (IsOpaqueAtClient((int)((candidate + 0.5) * PixelScale), (int)((row + 0.5) * PixelScale)))
                    {
                        x = rect.Left + (int)((candidate + 0.5) * PixelScale);
                        y = rect.Top + (int)(row * PixelScale);
                        return true;
                    }
            }
        return false;
    }

    private bool IsOverOpaqueScreenPixel(IntPtr lParam)
    {
        var point = new NativeMethods.Point { X = LoWord(lParam), Y = HiWord(lParam) };
        if (!NativeMethods.ScreenToClient(_handle, ref point)) return false;
        return IsOpaqueAtClient(point.X, point.Y);
    }

    private bool IsOpaqueAtClient(int clientX, int clientY)
    {
        var frame = _frame;
        if (frame is null) return false;

        var scale = PixelScale;
        if (scale <= 0) return false;

        var spriteX = (int)Math.Floor(clientX / scale);
        var spriteY = (int)Math.Floor(clientY / scale);

        if (_faceLeft) spriteX = _sheet.FrameWidth - 1 - spriteX;

        return _sheet.IsOpaque(frame.CellIndex, spriteX, spriteY);
    }

    private static int LoWord(IntPtr value) => unchecked((short)(value.ToInt64() & 0xFFFF));

    private static int HiWord(IntPtr value) => unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));

    private void OnClosed(object? sender, EventArgs e)
    {
        LocationChanged -= OnLocationChanged;
        SizeChanged -= OnSizeChanged;
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
