using System.Windows.Threading;
using System.Diagnostics;
using Microsoft.Win32;
using Petty.App.Desktop;
using Petty.App.Sprites;
using Petty.App.Views;
using Petty.Core.Animation;
using Petty.Core.Behavior;
using Petty.Core.Diagnostics;
using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.App.Pet;

/// <summary>
/// Owns the live session: feeds elapsed time into <see cref="PetRuntime"/>, maps the
/// behaviour state to a sprite clip and a motion target, and moves the window.
///
/// Two timers keep the work small and predictable:
///  - a ~20 ms frame timer advances the currently displayed animation;
///  - a ~200 ms simulation timer advances needs and the state machine.
/// Both are stopped when the session is disposed.
/// </summary>
public sealed class PetSession : IDisposable
{
    private static readonly TimeSpan MinFrameInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MaxFrameDelta = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ExitSafetyTimeout = TimeSpan.FromSeconds(20);

    private readonly PetRuntime _runtime;
    private readonly PetWindow _window;
    private readonly PropWindow _bowl;
    private readonly DesktopSurfaceController _surface;
    private readonly PetPropsController _props;
    private readonly IAppLog _log;
    private readonly AnimationController _animation;
    private readonly PetMotion _motion;
    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _simulationTimer;
    private readonly DispatcherTimer _hoverTimer;
    private InteractionMenuWindow? _menu;
    private int _menuAnchorX;

    /// <summary>
    /// How often the shell taskbar is re-read. Auto-hide reveals the bar within a
    /// few frames, so a quarter of a second keeps the pet out from under it without
    /// polling too often.
    /// </summary>
    private static readonly TimeSpan ShellBarPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _shellBarTimer;

    private readonly ActiveClock _clock = new(Timestamp());
    private TimeSpan _lastFrameTick;
    private TimeSpan _lastSimulationTick;
    private TimeSpan _exitStarted;
    private bool _started;
    private bool _exitRequested;
    private bool _exitCompleted;
    private bool _disposed;

    public PetSession(
        PetRuntime runtime,
        SpriteSheet sheet,
        PetWindow window,
        PropWindow bowl,
        PropWindow ball,
        DisplayService displays,
        PetTuning tuning,
        IAppLog log)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _bowl = bowl ?? throw new ArgumentNullException(nameof(bowl));
        ArgumentNullException.ThrowIfNull(ball);
        ArgumentNullException.ThrowIfNull(displays);
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _surface = new DesktopSurfaceController(displays, window.PhysicalWidth, log);
        _props = new PetPropsController(bowl, ball, _surface, window.PhysicalWidth);

        _animation = new AnimationController(sheet);
        _animation.FrameChanged += OnFrameChanged;

        _motion = new PetMotion(random: new Random());

        _frameTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = MinFrameInterval,
        };
        _frameTimer.Tick += OnFrameTick;

        _simulationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = tuning.TickInterval,
        };
        _simulationTimer.Tick += OnSimulationTick;

        _hoverTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(75) };
        _hoverTimer.Tick += OnPointerChanged;

        _shellBarTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = ShellBarPollInterval,
        };
        _shellBarTimer.Tick += OnShellBarTick;

        _window.PetClicked += OnPetClicked;
        _window.PointerChanged += OnPointerChanged;
        _window.GeometryChanged += OnWindowGeometryChanged;
        _bowl.Clicked += OnBowlClicked;
        _bowl.DragCompleted += OnBowlDragged;
    }

    public event EventHandler? ExitAnimationCompleted;

    /// <summary>Raised with a short reason whenever the saved state should be written now.</summary>
    public event EventHandler<string>? StateChanged;

    /// <summary>Current walkable band, for diagnostics.</summary>
    public MotionArea WalkArea => _surface.WalkArea;

    /// <summary>Last taskbar reading, for diagnostics (null when the shell has no bar).</summary>
    public ShellBarSnapshot? ShellBar => _surface.ShellBar;

    public void Start()
    {
        if (_started) return;
        if (_disposed) throw new ObjectDisposedException(nameof(PetSession));

        _started = true;

        _runtime.Behavior.Transitioned += OnBehaviorTransition;

        _surface.Load();
        ApplySurfaceChange(SurfaceChange.Display);

        _menu = new InteractionMenuWindow(_log);
        _menu.ActionRequested += OnActionRequested;
        _menu.PointerChanged += OnPointerChanged;
        _menu.Show();
        _menu.Attach(_window.Attachment.Mode, _window.Handle);
        _menu.Hide();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _lastFrameTick = _clock.Read(Timestamp());
        _lastSimulationTick = _lastFrameTick;

        _animation.Play(PetAnimationMap.ClipFor(_runtime.Behavior.State));

        _props.ShowBowlFilled(_runtime.Bowl.HasFood);
        _props.HideBall();

        _frameTimer.Start();
        _simulationTimer.Start();
        _shellBarTimer.Start();
        _hoverTimer.Start();

        _runtime.Behavior.StartEntering();
    }

    /// <summary>Starts the animated exit; <see cref="ExitAnimationCompleted"/> fires at the screen edge.</summary>
    public bool RequestExit()
    {
        if (_disposed || _exitRequested) return false;

        _exitRequested = true;
        _exitStarted = _clock.Read(Timestamp());

        if (!_runtime.RequestExit())
        {
            _log.Warn("Exit was requested while the pet was already exiting.");
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        _frameTimer.Stop();
        _simulationTimer.Stop();
        _shellBarTimer.Stop();
        _hoverTimer.Stop();

        _frameTimer.Tick -= OnFrameTick;
        _simulationTimer.Tick -= OnSimulationTick;
        _shellBarTimer.Tick -= OnShellBarTick;
        _hoverTimer.Tick -= OnPointerChanged;

        _runtime.Behavior.Transitioned -= OnBehaviorTransition;
        _window.PetClicked -= OnPetClicked;
        _window.PointerChanged -= OnPointerChanged;
        _window.GeometryChanged -= OnWindowGeometryChanged;
        _bowl.Clicked -= OnBowlClicked;
        _bowl.DragCompleted -= OnBowlDragged;
        _animation.FrameChanged -= OnFrameChanged;

        if (_menu is not null)
        {
            _menu.ActionRequested -= OnActionRequested;
            _menu.PointerChanged -= OnPointerChanged;
            _menu.Close();
            _menu = null;
        }

        _props.HideBall();
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        if (_disposed) return;

        var elapsed = _clock.Read(Timestamp());
        var delta = elapsed - _lastFrameTick;
        _lastFrameTick = elapsed;

        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        if (delta > MaxFrameDelta) delta = MaxFrameDelta;

        _animation.Advance(delta);

        var wasMoving = _motion.IsMoving;
        var moved = _motion.Update(delta);
        var climbed = _surface.AdvanceFloor(delta);

        if (wasMoving && !_motion.IsMoving && !_exitRequested)
            _runtime.NotifyArrived();

        if (moved || climbed)
        {
            FaceMovement();
            ApplyPosition();
        }

        if (climbed) _props.ApplyBowlPosition(_runtime.Snapshot.Placement);

        UpdateBallIfPlaying();

        if (_menu?.IsVisible == true)
        {
            PlaceInteractionMenu();
            UpdatePointer();
        }

        SyncFrameInterval();
    }

    /// <summary>
    /// A still pet ticks at its clip's own frame rate; movement and the floor easing
    /// keep the fast timer so both stay smooth. Playback is time-based either way.
    /// </summary>
    private void SyncFrameInterval()
    {
        var busy = _motion.IsMoving || _surface.IsFloorMoving || _menu?.IsVisible == true;
        var fps = _animation.CurrentClip?.FramesPerSecond ?? 0;

        var interval = busy || fps <= 0
            ? MinFrameInterval
            : TimeSpan.FromSeconds(1 / Math.Clamp(fps, 1, 60));

        if (interval > MaxFrameDelta) interval = MaxFrameDelta;
        if (_frameTimer.Interval != interval) _frameTimer.Interval = interval;
    }

    private void OnShellBarTick(object? sender, EventArgs e)
    {
        if (!_disposed) ApplySurfaceChange(_surface.Poll());
    }

    private void OnSimulationTick(object? sender, EventArgs e)
    {
        if (_disposed) return;

        var elapsed = _clock.Read(Timestamp());
        var delta = elapsed - _lastSimulationTick;
        _lastSimulationTick = elapsed;

        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        _runtime.Update(delta, DateTimeOffset.UtcNow);
        _props.ShowBowlFilled(_runtime.Bowl.HasFood);

        if (!_exitRequested || _exitCompleted) return;

        var timedOut = elapsed - _exitStarted > ExitSafetyTimeout;
        var offScreen = _runtime.IsExitAnimationComplete && !_motion.IsMoving;

        if (!offScreen && !timedOut) return;

        _exitCompleted = true;
        _log.Info(timedOut
            ? "Exit animation timed out; finishing shutdown."
            : "Exit animation completed; the pet reached the screen edge.");

        ExitAnimationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static TimeSpan Timestamp() => Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp());

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Suspend) _clock.Suspend(Timestamp());
        else if (e.Mode is PowerModes.Resume) _clock.Resume(Timestamp());
    }

    private void OnBehaviorTransition(object? sender, BehaviorTransition transition)
    {
        _log.Info($"Behaviour {transition.From} -> {transition.To} ({transition.Reason}).");

        _animation.Play(PetAnimationMap.ClipFor(transition.To));
        if (transition.To is PetActivity.Playing or PetActivity.Eating or PetActivity.Exiting)
        {
            HideInteractionMenu();
            _hoverTimer.Stop();
        }
        else if (_menu?.IsVisible != true) _hoverTimer.Start();

        switch (transition.To)
        {
            case PetActivity.Entering:
                _motion.Enter(WalkArea, _window.PhysicalWidth);
                break;

            case PetActivity.Walking:
                _motion.Wander(WalkArea, run: false);
                break;

            case PetActivity.Running:
                _motion.Wander(WalkArea, run: true);
                break;

            case PetActivity.SeekingFood:
                _motion.Seek(_props.SeekTargetX(_motion.X));
                break;

            case PetActivity.Playing:
                _props.ShowBall();
                UpdateBallIfPlaying();
                break;

            case PetActivity.Exiting:
                _props.HideBall();
                _motion.Exit(WalkArea, _window.PhysicalWidth);
                break;

            default:
                _motion.Stop();
                break;
        }

        if (transition.From is PetActivity.Playing)
        {
            _props.HideBall();
        }

        if (transition.From is PetActivity.Playing or PetActivity.Eating)
            StateChanged?.Invoke(this, transition.From is PetActivity.Eating ? "eat" : "play");

        FaceMovement();
        ApplyPosition();
        SyncFrameInterval();
    }

    private void OnFrameChanged(object? sender, EventArgs e)
    {
        if (_animation.CurrentFrame is { } frame)
            _window.ShowFrame(frame);
    }

    private void OnPetClicked(object? sender, EventArgs e)
    {
        _log.Info("Pet clicked.");
        _runtime.OnUserInteraction();
    }

    private void OnActionRequested(object? sender, PetInteraction action)
    {
        HideInteractionMenu();
        if (action is PetInteraction.Play)
        {
            _log.Info("Play requested from the pet.");
            _runtime.RequestPlay();
        }
    }

    private void OnPointerChanged(object? sender, EventArgs e) => UpdatePointer();

    private void UpdatePointer()
    {
        if (_disposed || _menu is null || !NativeMethods.GetCursorPos(out var point)) return;
        if (_runtime.Behavior.State is PetActivity.Eating or PetActivity.Playing or PetActivity.Exiting)
        {
            HideInteractionMenu();
            return;
        }
        var petRect = _window.PlacementRect;
        var insidePetBox = point.X >= petRect.Left && point.X < petRect.Left + petRect.Width &&
                           point.Y >= petRect.Top && point.Y < petRect.Top + petRect.Height;
        if (!insidePetBox && _menu.IsVisible != true) return;
        var underPointer = NativeMethods.WindowFromPoint(point);
        var overPet = InteractionMenuPlacement.IsPetHovered(
            new ScreenRect(petRect.Left, petRect.Top, petRect.Width, petRect.Height), point.X, point.Y,
            underPointer == _window.Handle || DesktopWindowService.IsDesktopSurface(underPointer));
        var overMenu = underPointer == _menu.Handle && _menu.ContainsScreenPoint(point.X, point.Y);
        if (!_menu.IsVisible && overPet)
        {
            _menuAnchorX = point.X - _window.PlacementRect.Left;
            PlaceInteractionMenu();
            _menu.Show();
            PlaceInteractionMenu();
            if (_window.Attachment.Mode != DesktopLayerMode.WorkerW) _menu.PlaceAbovePet(_window.Handle);
            _hoverTimer.Stop();
            SyncFrameInterval();
        }
        else if (_menu.IsVisible && !overPet && !overMenu) HideInteractionMenu();
    }

    private void HideInteractionMenu()
    {
        if (_menu?.IsVisible != true) return;
        _menu.Hide();
        if (_runtime.Behavior.State is not (PetActivity.Eating or PetActivity.Playing or PetActivity.Exiting))
            _hoverTimer.Start();
        SyncFrameInterval();
    }

    private void PlaceInteractionMenu()
    {
        if (_menu is null || _surface.PrimaryDisplay is not { } display ||
            !_window.TryGetMenuAnchor(_menuAnchorX, out var x, out var y)) return;
        var work = display.UsableArea;
        var top = work.Y;
        if (_surface.ShellBar is { Revealed: true } bar &&
            WalkSurface.EdgeOf(display.Bounds, WalkSurface.Intersect(display.Bounds, bar.Rect)) is ShellBarEdge.Top)
            top = Math.Max(top, bar.Rect.Bottom);
        var left = Math.Max(work.X, WalkArea.Left);
        var right = Math.Min(work.Right, WalkArea.Right + _window.PhysicalWidth);
        var bottom = Math.Min(work.Bottom, WalkArea.FloorY);
        var usable = new ScreenRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        var dpi = _window.DpiScale;
        _menu.Place(InteractionMenuPlacement.Place(usable, x, y,
            InteractionMenuWindow.MenuWidth * dpi, InteractionMenuWindow.MenuHeight * dpi));
    }

    private void OnBowlClicked(object? sender, EventArgs e)
    {
        var added = _runtime.RefillBowl();
        _props.ShowBowlFilled(_runtime.Bowl.HasFood);
        _log.Info($"Bowl filled with {added:F0}; food {_runtime.Bowl.Food:F0}/{_runtime.Bowl.Capacity:F0}.");

        if (added > 0) StateChanged?.Invoke(this, "feed");
    }

    private void OnBowlDragged(object? sender, EventArgs e)
    {
        var rect = _bowl.ScreenRect;

        var placement = _props.CaptureBowlPosition();

        _runtime.UpdateBowlPlacement(placement);
        _props.ApplyBowlPosition(_runtime.Snapshot.Placement);

        _log.Info($"Bowl dropped at x {rect.Left} ({(placement.RelativeX * 100):F0} % of {placement.DisplayDeviceName}).");
        StateChanged?.Invoke(this, "bowl-move");

        // The cat may be walking towards the old spot right now.
        if (_runtime.Behavior.State is PetActivity.SeekingFood) _motion.Seek(_props.SeekTargetX(_motion.X));
    }

    private void UpdateBallIfPlaying()
    {
        if (_runtime.Behavior.State is PetActivity.Playing)
            _props.UpdateBall(_motion.X, _runtime.Behavior.StateElapsed);
    }

    private void OnWindowGeometryChanged(object? sender, EventArgs e)
    {
        if (!_started || _disposed) return;
        _surface.Load();
        ApplySurfaceChange(SurfaceChange.Display);
    }

    private void ApplySurfaceChange(SurfaceChange change)
    {
        if (change is SurfaceChange.None) return;
        _motion.KeepInside(WalkArea);
        ApplyPosition();
        if (change is not SurfaceChange.Display) return;
        _props.ApplyBowlPosition(_runtime.Snapshot.Placement);
        if (_runtime.Behavior.State is PetActivity.SeekingFood) _motion.Seek(_props.SeekTargetX(_motion.X));
        UpdateBallIfPlaying();
    }

    private void FaceMovement() => _window.SetFacing(_motion.Facing < 0);

    private void ApplyPosition()
    {
        if (WalkArea.FloorY <= 0) return;

        _window.MoveTo(_motion.X, _surface.FloorY);
    }
}
