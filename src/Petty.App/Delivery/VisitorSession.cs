using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Petty.App.Desktop;
using Petty.App.Sprites;
using Petty.App.Views;
using Petty.Core.Diagnostics;
using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.App.Delivery;

/// <summary>One lightweight desktop guest. Owns no local pet, needs, props or saved state.</summary>
internal sealed class VisitorSession : IDisposable
{
    private readonly PetWindow _window;
    private readonly DesktopSurfaceController _surface;
    private readonly AnimationController _animation;
    private readonly SpriteSheet _sheet;
    private readonly DesktopLayerMode _layer;
    private readonly IAppLog _log;
    private readonly Func<IReadOnlyList<ScreenRect>> _obstacles;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
    private VisitorScene? _scene;
    private TimeSpan _lastTick;
    private TimeSpan _sincePoll;
    private bool _disposed;
    private bool _refreshing;

    public VisitorSession(SpriteSheet sheet, DisplayService displays, DesktopLayerMode layer, IAppLog log,
        Func<IReadOnlyList<ScreenRect>> obstacles)
    {
        _sheet = sheet;
        _layer = layer;
        _log = log;
        _obstacles = obstacles;
        _window = new PetWindow(sheet, log);
        _window.SetResourceReference(FrameworkElement.ToolTipProperty, "OnlineIncomingLetters");
        _surface = new DesktopSurfaceController(displays, _window.PhysicalWidth, log);
        _animation = new AnimationController(sheet);
        _animation.FrameChanged += FrameChanged;
        _window.PetClicked += Clicked;
        _window.GeometryChanged += GeometryChanged;
        _timer.Tick += Tick;
    }

    public event Action? LettersRequested;
    public event Action? Finished;
    public bool CanRead => _scene?.Phase is VisitorPhase.Waiting;

    public void Start()
    {
        _surface.Load();
        Scale();
        _scene = new VisitorScene(_surface.WalkArea, _surface.PrimaryDisplay!.Bounds, _window.PhysicalWidth,
            _obstacles(), TimeSpan.FromSeconds(_sheet.GetClip("mail-sit").Frames.Count / _sheet.GetClip("mail-sit").FramesPerSecond),
            TimeSpan.FromSeconds(4 / _sheet.GetClip("mail-exit").FramesPerSecond));
        _animation.Play(_scene.Clip);
        _window.Show();
        _window.AttachToDesktop(_layer, _log);
        Place();
        SystemEvents.PowerModeChanged += PowerChanged;
        _lastTick = Timestamp();
        _timer.Interval = TimeSpan.FromMilliseconds(20);
        _timer.Start();
    }

    public void RequestExit()
    {
        _scene?.RequestExit();
        if (_scene is not null)
        {
            _animation.Play(_scene.Clip);
            _timer.Interval = TimeSpan.FromMilliseconds(20);
        }
    }

    public void RestoreShell()
    {
        if (!_disposed)
        {
            _window.AttachToDesktop(_layer, _log);
            RefreshSurface(reload: true);
        }
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_disposed || _scene is null)
        {
            return;
        }
        TimeSpan now = Timestamp();
        TimeSpan delta = now - _lastTick;
        _lastTick = now;
        delta = TimeSpan.FromSeconds(Math.Clamp(delta.TotalSeconds, 0, 0.25));
        _sincePoll += delta;
        if (_sincePoll >= TimeSpan.FromMilliseconds(250))
        {
            _sincePoll = TimeSpan.Zero;
            RefreshSurface(reload: false);
        }
        _surface.AdvanceFloor(delta);
        _animation.Advance(delta);
        _scene.Advance(delta);
        _animation.Play(_scene.Clip);
        Place();
        if (_scene.Phase is VisitorPhase.Finished)
        {
            _timer.Stop();
            Finished?.Invoke();
            return;
        }
        _timer.Interval = _scene.Phase is VisitorPhase.Waiting && !_surface.IsFloorMoving
            ? TimeSpan.FromSeconds(1 / _sheet.GetClip("mail-idle").FramesPerSecond)
            : TimeSpan.FromMilliseconds(20);
    }

    private void RefreshSurface(bool reload)
    {
        if (_refreshing || _disposed)
        {
            return;
        }
        _refreshing = true;
        try
        {
            if (reload)
            {
                _surface.Load();
            }
            SurfaceChange change = reload ? SurfaceChange.Display : _surface.Poll();
            if (change is SurfaceChange.Display)
            {
                Scale();
            }
            _scene?.UpdateSurface(_surface.WalkArea, _surface.PrimaryDisplay!.Bounds, _window.PhysicalWidth, _obstacles());
            Place();
        }
        finally { _refreshing = false; }
    }

    private void Scale()
    {
        _window.SetSpriteScale(SpriteDisplayScale.ForDisplay(_surface.PrimaryDisplay!.Bounds));
        _surface.SetWindowWidth(_window.PhysicalWidth);
    }

    private void Place()
    {
        if (_scene is not null)
        {
            _window.SetFacing(_scene.FaceLeft);
            _window.MoveTo(_scene.X, _surface.FloorY);
        }
    }

    private void FrameChanged(object? sender, EventArgs e) => _window.ShowFrame(_animation.CurrentFrame!);
    private void Clicked(object? sender, EventArgs e)
    {
        if (CanRead)
        {
            LettersRequested?.Invoke();
        }
    }
    private void GeometryChanged(object? sender, EventArgs e) => RefreshSurface(reload: true);
    private static TimeSpan Timestamp() => Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp());
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) => _window.Dispatcher.BeginInvoke(() =>
    {
        if (_disposed)
        {
            return;
        }
        if (e.Mode is PowerModes.Suspend)
        {
            _timer.Stop();
        }
        else if (e.Mode is PowerModes.Resume && !_disposed)
        {
            _lastTick = Timestamp();
            _timer.Start();
        }
    });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Tick;
        SystemEvents.PowerModeChanged -= PowerChanged;
        _animation.FrameChanged -= FrameChanged;
        _window.PetClicked -= Clicked;
        _window.GeometryChanged -= GeometryChanged;
        _window.Close();
    }
}
