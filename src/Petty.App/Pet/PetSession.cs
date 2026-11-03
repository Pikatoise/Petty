using System.Diagnostics;
using System.Windows.Threading;
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
/// An adaptive frame timer advances animation and movement; separate simulation,
/// hover and taskbar timers track needs, input and desktop changes.
/// All timers are stopped when the session is disposed.
/// </summary>
public sealed class PetSession : IDisposable
{
    private static readonly TimeSpan MinFrameInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MaxFrameDelta = TimeSpan.FromMilliseconds(250);
    private TimeSpan _exitSafetyTimeout = TimeSpan.FromSeconds(20);

    private readonly PetRuntime _runtime;
    private readonly PetWindow _window;
    private readonly PropWindow _bowl;
    private readonly DesktopSurfaceController _surface;
    private readonly PetPropsController _props;
    private readonly BowlDisplay _bowlDisplay;
    private TimeSpan _bowlDropDuration;
    private readonly IAppLog _log;
    private readonly AnimationController _animation;
    private readonly PetMotion _motion;
    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _simulationTimer;
    private readonly DispatcherTimer _hoverTimer;
    private InteractionMenuWindow? _menu;
    private int _menuAnchorX;
    private TimeSpan _sceneStarted;
    private TimeSpan _sceneDuration;
    private int _sceneDirection;
    private double _sceneOrigin;

    /// <summary>
    /// How often the shell taskbar is re-read. Auto-hide reveals the bar within a
    /// few frames, so a quarter of a second keeps the pet out from under it without
    /// polling too often.
    /// </summary>
    private static readonly TimeSpan ShellBarPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _shellBarTimer;

    private readonly ActiveClock _clock = new(Timestamp());
    private TimeSpan _lastFrameTick;
    private TimeSpan _lastFrameRealTick;
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
        PropWindow hand,
        DisplayService displays,
        string assetDirectory,
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
        _props = new PetPropsController(bowl, ball, hand, _surface, window.PhysicalWidth,
            sheet.Metadata.PlayContactX ?? 58, sheet.Metadata.PlayContactY ?? 45, sheet.GroundRow,
            sheet.Metadata.SpeciesId == "dog" ? 50 : 54);
        _bowlDisplay = new BowlDisplay(bowl, _surface, assetDirectory, window.Attachment.Mode, log);
        _bowlDropDuration = tuning.Sanitized().BowlDropDuration;

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

#if DEBUG
    public string DebugClip => _animation.CurrentClip?.Name ?? "—";
    public double DebugFps => _animation.CurrentClip?.FramesPerSecond ?? 0;
    private double _debugTimeScale = 1;
    public void ApplyDebugTuning(PetTuning tuning, double timeScale)
    {
        _runtime.SetTuning(tuning);
        _bowlDropDuration = tuning.Sanitized().BowlDropDuration;
        if (!_exitRequested)
        {
            _clock.SetSpeed(timeScale, Timestamp());
            _debugTimeScale = timeScale;
            if (_started)
            {
                SyncFrameInterval();
            }
        }
    }
    public void RefreshDebugBowl() => _bowlDisplay.UpdateLevel(_runtime.Bowl.Food, _runtime.Bowl.Capacity);
#endif

    public event EventHandler? ExitAnimationCompleted;

    /// <summary>Raised with a short reason whenever the saved state should be written now.</summary>
    public event EventHandler<string>? StateChanged;

    /// <summary>Current walkable band, for diagnostics.</summary>
    public MotionArea WalkArea => _surface.WalkArea;

    /// <summary>Last taskbar reading, for diagnostics (null when the shell has no bar).</summary>
    public ShellBarSnapshot? ShellBar => _surface.ShellBar;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PetSession));
        }

        _started = true;

        _runtime.Behavior.Transitioned += OnBehaviorTransition;

        _surface.Load();
        UpdateSpriteScale();
        _props.ApplyBowlPosition(_runtime.Snapshot.Placement);

        _menu = new InteractionMenuWindow(_log);
        _menu.ActionRequested += OnActionRequested;
        _menu.PointerChanged += OnPointerChanged;
        _menu.Show();
        _menu.Attach(_window.Attachment.Mode, _window.Handle);
        _menu.Hide();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        TimeSpan startedAt = Timestamp();
        _lastFrameRealTick = _clock.Read(startedAt);
        _lastFrameTick = SimulationTime(startedAt);
        _lastSimulationTick = _lastFrameTick;

        _animation.Play(PetAnimationMap.ClipFor(_runtime.Behavior.State));

        _bowlDisplay.UpdateLevel(_runtime.Bowl.Food, _runtime.Bowl.Capacity);
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
        if (_disposed || _exitRequested)
        {
            return false;
        }

        _exitRequested = true;
        _bowlDisplay.Stop();
        TimeSpan exitAt = Timestamp();
        _exitStarted = _clock.Read(exitAt);
#if DEBUG
        _clock.SetSpeed(1, exitAt);
        _debugTimeScale = 1;
#endif

        if (!_runtime.RequestExit())
        {
            _log.Warn("Exit was requested while the pet was already exiting.");
        }
        _exitSafetyTimeout = TimeSpan.FromSeconds(Math.Max(20, Math.Abs(_motion.TargetX - _motion.X) / PetMotion.ExitSpeed + 5));

        return true;
    }

    public void RestoreShell()
    {
        if (_disposed)
        {
            return;
        }

        HideInteractionMenu();
        _menu?.Attach(_window.Attachment.Mode, _window.Handle);
        ApplySurfaceChange(_surface.Poll());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

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

        _props.HideScenes();
        _bowlDisplay.Dispose();
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        TimeSpan now = Timestamp();
        TimeSpan realElapsed = _clock.Read(now);
        TimeSpan elapsed = SimulationTime(now);
        TimeSpan realDelta = realElapsed - _lastFrameRealTick;
        TimeSpan delta = elapsed - _lastFrameTick;
        _lastFrameRealTick = realElapsed;
        _lastFrameTick = elapsed;

        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (realDelta < TimeSpan.Zero)
        {
            realDelta = TimeSpan.Zero;
        }

        if (realDelta > MaxFrameDelta)
        {
            delta = TimeSpan.FromTicks((long)(delta.Ticks * (MaxFrameDelta.TotalSeconds / realDelta.TotalSeconds)));
            realDelta = MaxFrameDelta;
        }

        if (_runtime.Behavior.State is not PetActivity.Playing)
        {
            _animation.Advance(delta);
        }

        bool wasMoving = _motion.IsMoving;
        bool moved = _motion.Update(delta);
        bool climbed = _surface.AdvanceFloor(realDelta);
        _props.AdvanceBowlDrop(realDelta, _bowlDropDuration);

        bool bowlReady = !_bowl.IsDragging && !_props.IsBowlMoving;
        if (!_motion.IsMoving && !_exitRequested &&
            (_runtime.Behavior.State is PetActivity.SeekingFood ? bowlReady : wasMoving))
        {
            _runtime.NotifyArrived();
        }

        if (moved || climbed)
        {
            if (_runtime.Behavior.State is not (PetActivity.Playing or PetActivity.Petting or PetActivity.Eating))
            {
                FaceMovement();
            }

            ApplyPosition();
        }

        if (climbed && !_props.IsBowlMoving)
        {
            _props.ApplyBowlPosition(_runtime.Snapshot.Placement);
        }

        UpdateScene();

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
        bool busy = _runtime.Behavior.State is PetActivity.Playing or PetActivity.Petting || _motion.IsMoving || _surface.IsFloorMoving || _props.IsBowlMoving || _menu?.IsVisible == true;
        double fps = _animation.CurrentClip?.FramesPerSecond ?? 0;
#if DEBUG
        fps *= _debugTimeScale;
#endif

        TimeSpan interval = busy || fps <= 0
            ? MinFrameInterval
            : TimeSpan.FromSeconds(1 / Math.Clamp(fps, 1, 60));

        if (interval > MaxFrameDelta)
        {
            interval = MaxFrameDelta;
        }

        if (_frameTimer.Interval != interval)
        {
            _frameTimer.Interval = interval;
        }
    }

    private void OnShellBarTick(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            ApplySurfaceChange(_surface.Poll());
        }
    }

    private void OnSimulationTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        TimeSpan now = Timestamp();
        TimeSpan realElapsed = _clock.Read(now);
        TimeSpan elapsed = SimulationTime(now);
        TimeSpan delta = elapsed - _lastSimulationTick;
        _lastSimulationTick = elapsed;

        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        _runtime.Update(delta, DateTimeOffset.UtcNow);
        _bowlDisplay.UpdateLevel(_runtime.Bowl.Food, _runtime.Bowl.Capacity);

        if (!_exitRequested || _exitCompleted)
        {
            return;
        }

        bool timedOut = realElapsed - _exitStarted > _exitSafetyTimeout;
        bool offScreen = _runtime.IsExitAnimationComplete && !_motion.IsMoving;

        if (!offScreen && !timedOut)
        {
            return;
        }

        _exitCompleted = true;
        _log.Info(timedOut
            ? "Exit animation timed out; finishing shutdown."
            : "Exit animation completed; the pet reached the screen edge.");

        ExitAnimationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private TimeSpan SimulationTime(TimeSpan timestamp) =>
#if DEBUG
        _clock.ReadScaled(timestamp);
#else
        _clock.Read(timestamp);
#endif


    private static TimeSpan Timestamp() => Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp());

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Suspend)
        {
            _clock.Suspend(Timestamp());
        }
        else if (e.Mode is PowerModes.Resume)
        {
            _clock.Resume(Timestamp());
        }
    }

    private void OnBehaviorTransition(object? sender, BehaviorTransition transition)
    {
        _log.Info($"Behaviour {transition.From} -> {transition.To} ({transition.Reason}).");

        _bowl.SetDraggingEnabled(transition.To is not (PetActivity.Eating or PetActivity.Exiting));
        if (transition.To is PetActivity.Eating)
        {
            _props.ApplyBowlPosition(_runtime.Snapshot.Placement);
        }

        _animation.Play(PetAnimationMap.ClipFor(transition.To));
        _menu?.SetPlayEnabled(_runtime.Behavior.CanPlay);
        if (transition.To is PetActivity.Playing or PetActivity.Petting or PetActivity.Eating or PetActivity.Exiting)
        {
            HideInteractionMenu();
            _hoverTimer.Stop();
        }
        else if (_menu?.IsVisible != true)
        {
            _hoverTimer.Start();
        }

        switch (transition.To)
        {
            case PetActivity.Entering:
                _motion.Enter(WalkArea, _window.PhysicalWidth, _surface.PrimaryDisplay?.Bounds.X);
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

            case PetActivity.Eating:
                _motion.Stop();
                // A small backward positioning step must not leave the pet
                // facing away from the food once it starts eating.
                _window.SetFacing(_props.IsBowlLeft(_motion.X));
                break;

            case PetActivity.Playing:
            case PetActivity.Petting:
                _motion.Stop();
                _sceneStarted = SimulationTime(Timestamp());
                _sceneDuration = _runtime.Behavior.InteractionDuration;
                _sceneOrigin = _motion.X;
                _sceneDirection = _motion.X < (WalkArea.Left + WalkArea.Right) / 2 ? 1 : -1;
                _window.SetFacing(_sceneDirection < 0);
                if (transition.To is PetActivity.Playing)
                {
                    _props.ShowBall();
                }
                else
                {
                    _props.ShowHand();
                }

                UpdateScene();
                break;

            case PetActivity.Exiting:
                _props.HideBall();
                _motion.Exit(WalkArea, _window.PhysicalWidth, screenLeft: _surface.PrimaryDisplay?.Bounds.X, screenRight: _surface.PrimaryDisplay?.Bounds.Right);
                break;

            default:
                _motion.Stop();
                break;
        }

        if (transition.From is PetActivity.Playing or PetActivity.Petting)
        {
            _props.HideScenes();
        }

        if (transition.From is PetActivity.Playing or PetActivity.Petting or PetActivity.Eating)
        {
            StateChanged?.Invoke(this, transition.From is PetActivity.Eating ? "eat" : "play");
        }

        if (transition.To is not (PetActivity.Playing or PetActivity.Petting or PetActivity.Eating))
        {
            FaceMovement();
        }

        ApplyPosition();
        SyncFrameInterval();
    }

    private void OnFrameChanged(object? sender, EventArgs e)
    {
        if (_animation.CurrentFrame is { } frame)
        {
            _window.ShowFrame(frame);
        }
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
        else if (action is PetInteraction.Pet)
        {
            _runtime.RequestPet();
        }
    }

    private void OnPointerChanged(object? sender, EventArgs e) => UpdatePointer();

    private void UpdatePointer()
    {
        if (_disposed || _menu is null || !NativeMethods.GetCursorPos(out NativeMethods.Point point))
        {
            return;
        }

        if (_runtime.Behavior.State is PetActivity.Eating or PetActivity.Playing or PetActivity.Petting or PetActivity.Exiting)
        {
            HideInteractionMenu();
            return;
        }
        _menu.SetPlayEnabled(_runtime.Behavior.CanPlay);
        (int Left, int Top, int Width, int Height) petRect = _window.PlacementRect;
        bool insidePetBox = point.X >= petRect.Left && point.X < petRect.Left + petRect.Width &&
                           point.Y >= petRect.Top && point.Y < petRect.Top + petRect.Height;
        if (!insidePetBox && _menu.IsVisible != true)
        {
            return;
        }

        nint underPointer = NativeMethods.WindowFromPoint(point);
        bool overPet = InteractionMenuPlacement.IsPetHovered(
            new ScreenRect(petRect.Left, petRect.Top, petRect.Width, petRect.Height), point.X, point.Y,
            underPointer == _window.Handle || DesktopWindowService.IsDesktopSurface(underPointer));
        bool overMenu = underPointer == _menu.Handle && _menu.ContainsScreenPoint(point.X, point.Y);
        if (!_menu.IsVisible && overPet)
        {
            _menuAnchorX = point.X - _window.PlacementRect.Left;
            PlaceInteractionMenu();
            _menu.Show();
            PlaceInteractionMenu();
            if (_window.Attachment.Mode != DesktopLayerMode.WorkerW)
            {
                _menu.PlaceAbovePet(_window.Handle);
            }

            _hoverTimer.Stop();
            SyncFrameInterval();
        }
        else if (_menu.IsVisible && !overPet && !overMenu)
        {
            HideInteractionMenu();
        }
    }

    private void HideInteractionMenu()
    {
        if (_menu?.IsVisible != true)
        {
            return;
        }

        _menu.Hide();
        if (_runtime.Behavior.State is not (PetActivity.Eating or PetActivity.Playing or PetActivity.Petting or PetActivity.Exiting))
        {
            _hoverTimer.Start();
        }

        SyncFrameInterval();
    }

    private void PlaceInteractionMenu()
    {
        if (_menu is null || _surface.PrimaryDisplay is not { } display ||
            !_window.TryGetMenuAnchor(_menuAnchorX, out int x, out int y))
        {
            return;
        }

        ScreenRect work = display.UsableArea;
        double top = work.Y;
        if (_surface.ShellBar is { Revealed: true } bar &&
            WalkSurface.EdgeOf(display.Bounds, WalkSurface.Intersect(display.Bounds, bar.Rect)) is ShellBarEdge.Top)
        {
            top = Math.Max(top, bar.Rect.Bottom);
        }

        double left = Math.Max(work.X, WalkArea.Left);
        double right = Math.Min(work.Right, WalkArea.Right + _window.PhysicalWidth);
        double bottom = Math.Min(work.Bottom, WalkArea.FloorY);
        var usable = new ScreenRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        double dpi = _window.DpiScale;
        double menuScale = InteractionMenuPlacement.ScaleForDisplay(SpriteDisplayScale.ForDisplay(display.Bounds), dpi);
        _menu.Place(InteractionMenuPlacement.Place(usable, x, y,
            InteractionMenuWindow.MenuWidth * dpi * menuScale, InteractionMenuWindow.MenuHeight * dpi * menuScale));
    }

    private void OnBowlClicked(object? sender, EventArgs e)
    {
        if (_exitRequested || !_bowlDisplay.TryPour(_runtime.Bowl.Food, _runtime.Bowl.Capacity))
        {
            return;
        }

        double added = _runtime.RefillBowl();
        _bowlDisplay.UpdateLevel(_runtime.Bowl.Food, _runtime.Bowl.Capacity);
        _log.Info($"Bowl filled with {added:F0}; food {_runtime.Bowl.Food:F0}/{_runtime.Bowl.Capacity:F0}.");

        if (added > 0)
        {
            StateChanged?.Invoke(this, "feed");
        }
    }

    private void OnBowlDragged(object? sender, EventArgs e)
    {
        (int Left, int Top, int Width, int Height) rect = _bowl.ScreenRect;

        BowlPlacementState placement = _props.CaptureBowlPosition();

        _runtime.UpdateBowlPlacement(placement);
        _props.StartBowlDrop(placement);
        SyncFrameInterval();

        _log.Info($"Bowl dropped at x {rect.Left} ({(placement.RelativeX * 100):F0} % of {placement.DisplayDeviceName}).");
        StateChanged?.Invoke(this, "bowl-move");

        // The cat may be walking towards the old spot right now.
        if (_runtime.Behavior.State is PetActivity.SeekingFood)
        {
            _motion.Seek(_props.SeekTargetX(_motion.X));
        }
    }

    private void UpdateScene()
    {
        PetActivity activity = _runtime.Behavior.State;
        if (activity is not (PetActivity.Playing or PetActivity.Petting))
        {
            return;
        }

        double elapsed = (SimulationTime(Timestamp()) - _sceneStarted).TotalSeconds;
        double progress = elapsed / Math.Max(0.001, _sceneDuration.TotalSeconds);
        if (activity is PetActivity.Playing)
        {
            if (_animation.CurrentClip is not { } clip)
            {
                return;
            }

            PlayScenePose pose = ActionScene.SamplePlay(elapsed, _sceneDuration.TotalSeconds,
                clip.Frames.Count, clip.FramesPerSecond, _props.PlayExitDistance(_sceneOrigin, _sceneDirection), _props.PlayContactLift);
            _animation.ShowSceneFrame(pose.FrameIndex);
            _props.UpdatePlay(_motion.X, _sceneDirection, pose);
        }
        else
        {
            _props.UpdateScene(activity, _motion.X, _sceneDirection, progress);
        }
    }

    private void OnWindowGeometryChanged(object? sender, EventArgs e)
    {
        if (!_started || _disposed)
        {
            return;
        }

        _surface.Load();
        ApplySurfaceChange(SurfaceChange.Display);
    }

    private void ApplySurfaceChange(SurfaceChange change)
    {
        if (change is SurfaceChange.None)
        {
            return;
        }

        if (change is SurfaceChange.Display)
        {
            UpdateSpriteScale();
        }

        if (_runtime.Behavior.State is PetActivity.Exiting)
        {
            _motion.Exit(WalkArea, _window.PhysicalWidth, _motion.Facing, _surface.PrimaryDisplay?.Bounds.X, _surface.PrimaryDisplay?.Bounds.Right);
            _exitSafetyTimeout = TimeSpan.FromSeconds(Math.Max(_exitSafetyTimeout.TotalSeconds,
                (_clock.Read(Timestamp()) - _exitStarted).TotalSeconds + Math.Abs(_motion.TargetX - _motion.X) / PetMotion.ExitSpeed + 5));
        }
        else if (_runtime.Behavior.State is PetActivity.Entering)
        {
            _motion.MoveTo(Math.Clamp(_motion.TargetX, WalkArea.Left, WalkArea.Right), PetMotion.EnterSpeed);
        }
        else
        {
            _motion.KeepInside(WalkArea);
        }

        ApplyPosition();
        if (change is not SurfaceChange.Display)
        {
            return;
        }

        _runtime.Behavior.CancelInteraction();
        _props.ApplyBowlPosition(_runtime.Snapshot.Placement);
        if (_runtime.Behavior.State is PetActivity.SeekingFood)
        {
            _motion.Seek(_props.SeekTargetX(_motion.X));
        }
        else if (_runtime.Behavior.State is PetActivity.Eating)
        {
            _motion.Place(_props.SeekTargetX(_motion.X));
            _window.SetFacing(_props.IsBowlLeft(_motion.X));
            ApplyPosition();
        }

        UpdateScene();
    }

    private void UpdateSpriteScale()
    {
        if (_surface.PrimaryDisplay is not { } display)
        {
            return;
        }

        double scale = SpriteDisplayScale.ForDisplay(display.Bounds);
        _window.SetSpriteScale(scale);
        _props.SetSpriteScale(scale, _window.PhysicalWidth);
        _surface.SetWindowWidth(_window.PhysicalWidth);
    }

    private void FaceMovement() => _window.SetFacing(_motion.Facing < 0);

    private void ApplyPosition()
    {
        if (WalkArea.FloorY <= 0)
        {
            return;
        }

        _window.MoveTo(_motion.X, _surface.FloorY);
    }
}
