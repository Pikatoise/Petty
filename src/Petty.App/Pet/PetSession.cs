using System.Windows.Threading;
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
    private readonly DisplayService _displays;
    private readonly IAppLog _log;
    private readonly AnimationController _animation;
    private readonly PetMotion _motion;
    private readonly FloorFollow _floor = new();
    private readonly ShellBarProbe _shellBars = new();
    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _simulationTimer;

    /// <summary>
    /// How often the shell taskbar is re-read. Auto-hide reveals the bar within a
    /// few frames, so a quarter of a second keeps the pet out from under it without
    /// polling too often.
    /// </summary>
    private static readonly TimeSpan ShellBarPollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Display layout is re-read every 16 polls (~4 s).</summary>
    private const int DisplayReloadTickCount = 16;

    private readonly DispatcherTimer _shellBarTimer;
    private int _displayReloadTicks = DisplayReloadTickCount;

    private IReadOnlyList<DisplayInfo> _displayLayout = Array.Empty<DisplayInfo>();
    private DisplayInfo? _display;
    private ShellBarSnapshot? _shellBar;
    private MotionArea _area;
    private DateTimeOffset _lastFrameTick;
    private DateTimeOffset _lastSimulationTick;
    private DateTimeOffset _exitStartedUtc;
    private bool _started;
    private bool _exitRequested;
    private bool _exitCompleted;
    private bool _disposed;

    public PetSession(
        PetRuntime runtime,
        SpriteSheet sheet,
        PetWindow window,
        DisplayService displays,
        PetTuning tuning,
        IAppLog log)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _displays = displays ?? throw new ArgumentNullException(nameof(displays));
        _log = log ?? throw new ArgumentNullException(nameof(log));

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

        _shellBarTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = ShellBarPollInterval,
        };
        _shellBarTimer.Tick += OnShellBarTick;

        _window.PetClicked += OnPetClicked;
        _window.GeometryChanged += OnWindowGeometryChanged;
    }

    public event EventHandler? ExitAnimationCompleted;

    public PetActivity CurrentActivity => _runtime.Behavior.State;

    public bool IsExiting => _exitRequested;

    /// <summary>Current walkable band, for diagnostics.</summary>
    public MotionArea WalkArea => _area;

    /// <summary>Last taskbar reading, for diagnostics (null when the shell has no bar).</summary>
    public ShellBarSnapshot? ShellBar => _shellBar;

    public void Start()
    {
        if (_started) return;
        if (_disposed) throw new ObjectDisposedException(nameof(PetSession));

        _started = true;

        _runtime.Behavior.Transitioned += OnBehaviorTransition;

        RefreshDisplays();

        _lastFrameTick = DateTimeOffset.UtcNow;
        _lastSimulationTick = _lastFrameTick;

        _animation.Play(PetAnimationMap.ClipFor(_runtime.Behavior.State));

        _frameTimer.Start();
        _simulationTimer.Start();
        _shellBarTimer.Start();

        _runtime.Behavior.StartEntering();
    }

    /// <summary>Starts the animated exit; <see cref="ExitAnimationCompleted"/> fires once it is off-screen.</summary>
    public bool RequestExit()
    {
        if (_disposed || _exitRequested) return false;

        _exitRequested = true;
        _exitStartedUtc = DateTimeOffset.UtcNow;

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

        _frameTimer.Stop();
        _simulationTimer.Stop();
        _shellBarTimer.Stop();

        _frameTimer.Tick -= OnFrameTick;
        _simulationTimer.Tick -= OnSimulationTick;
        _shellBarTimer.Tick -= OnShellBarTick;

        _runtime.Behavior.Transitioned -= OnBehaviorTransition;
        _window.PetClicked -= OnPetClicked;
        _window.GeometryChanged -= OnWindowGeometryChanged;
        _animation.FrameChanged -= OnFrameChanged;
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        if (_disposed) return;

        var now = DateTimeOffset.UtcNow;
        var delta = now - _lastFrameTick;
        _lastFrameTick = now;

        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        if (delta > MaxFrameDelta) delta = MaxFrameDelta;

        _animation.Advance(delta);

        var wasMoving = _motion.IsMoving;
        var moved = _motion.Update(delta);
        var climbed = AdvanceFloor(delta);

        if (wasMoving && !_motion.IsMoving && !_exitRequested)
            _runtime.NotifyArrived();

        if (moved || climbed)
        {
            FaceMovement();
            ApplyPosition();
        }

        SyncFrameInterval();
    }

    /// <summary>
    /// A still pet ticks at its clip's own frame rate; movement and the floor easing
    /// keep the fast timer so both stay smooth. Playback is time-based either way.
    /// </summary>
    private void SyncFrameInterval()
    {
        var busy = _motion.IsMoving || (_area.FloorY > 0 && !_floor.IsAt(_area.FloorY));
        var fps = _animation.CurrentClip?.FramesPerSecond ?? 0;

        var interval = busy || fps <= 0
            ? MinFrameInterval
            : TimeSpan.FromSeconds(1 / Math.Clamp(fps, 1, 60));

        if (interval > MaxFrameDelta) interval = MaxFrameDelta;
        if (_frameTimer.Interval != interval) _frameTimer.Interval = interval;
    }

    /// <summary>Eases the paws onto the current target floor; true when the row changed.</summary>
    private bool AdvanceFloor(TimeSpan delta)
    {
        if (_area.FloorY <= 0) return false;

        var before = _floor.Current;
        _floor.Advance(_area.FloorY, delta);
        return Math.Abs(_floor.Current - before) > 0.01;
    }

    private void OnShellBarTick(object? sender, EventArgs e)
    {
        if (_disposed) return;

        // A monitor can be hot-plugged or resized at any time; re-reading the layout
        // every few seconds keeps the walk band from going stale. EnumDisplayMonitors
        // is cheap but not free, so it does not run on every tick.
        if (--_displayReloadTicks <= 0)
        {
            _displayReloadTicks = DisplayReloadTickCount;
            ReloadDisplaysIfChanged();
        }

        RefreshWalkArea();
    }

    private void ReloadDisplaysIfChanged()
    {
        IReadOnlyList<DisplayInfo> displays;

        try
        {
            displays = _displays.GetDisplays();
        }
        catch (InvalidOperationException ex)
        {
            _log.Warn($"Could not re-read the display layout: {ex.Message}");
            return;
        }

        if (LayoutEquals(_displayLayout, displays)) return;

        _displayLayout = displays;
        _display = _displays.Primary(displays);

        _log.Info("Display layout changed: " +
                  string.Join(", ", displays.Select(d => $"{d.DeviceName} {FormatRect(d.Bounds)}")) + ".");

        RefreshWalkArea(force: true);
    }

    private static bool LayoutEquals(IReadOnlyList<DisplayInfo> a, IReadOnlyList<DisplayInfo> b)
    {
        if (a.Count != b.Count) return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }

        return true;
    }

    private void OnSimulationTick(object? sender, EventArgs e)
    {
        if (_disposed) return;

        var now = DateTimeOffset.UtcNow;
        var delta = now - _lastSimulationTick;
        _lastSimulationTick = now;

        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        _runtime.Update(delta, now);

        if (!_exitRequested || _exitCompleted) return;

        var timedOut = now - _exitStartedUtc > ExitSafetyTimeout;
        var offScreen = _runtime.IsExitAnimationComplete && !_motion.IsMoving;

        if (!offScreen && !timedOut) return;

        _exitCompleted = true;
        _log.Info(timedOut
            ? "Exit animation timed out; finishing shutdown."
            : "Exit animation completed; the pet has left the screen.");

        ExitAnimationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OnBehaviorTransition(object? sender, BehaviorTransition transition)
    {
        _log.Info($"Behaviour {transition.From} -> {transition.To} ({transition.Reason}).");

        _animation.Play(PetAnimationMap.ClipFor(transition.To));

        switch (transition.To)
        {
            case PetActivity.Entering:
                _motion.Enter(_area, _window.PhysicalWidth);
                break;

            case PetActivity.Walking:
                _motion.Wander(_area, run: false);
                break;

            case PetActivity.Running:
                _motion.Wander(_area, run: true);
                break;

            case PetActivity.SeekingFood:
                // Stage 5 replaces this with the bowl's position.
                _motion.Seek(_area.Left + (_area.Width * 0.55));
                break;

            case PetActivity.Exiting:
                _motion.Exit(_area, _window.PhysicalWidth);
                break;

            default:
                _motion.Stop();
                break;
        }

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

    private void OnWindowGeometryChanged(object? sender, EventArgs e)
    {
        if (!_started || _disposed) return;

        RefreshDisplays();
        _motion.KeepInside(_area);
        _floor.Snap(_area.FloorY);
        ApplyPosition();
    }

    private void RefreshDisplays()
    {
        _displayLayout = _displays.GetDisplays();
        _display = _displays.Primary(_displayLayout);
        RefreshWalkArea(force: true);
    }

    /// <summary>
    /// Recomputes the walkable band from the monitor geometry and the taskbar that is
    /// currently drawn on screen. A hidden auto-hide bar is passed as null, which lets
    /// the pet walk along the very bottom edge; when the bar slides up the pet steps
    /// on top of it instead of being covered by it.
    /// </summary>
    private void RefreshWalkArea(bool force = false)
    {
        if (_disposed) return;

        var display = _display ?? _displays.Primary(_displayLayout);
        if (display is null) return;

        _shellBar = _shellBars.Read(display);
        var visibleBar = _shellBar is { Revealed: true } bar ? bar.Rect : (ScreenRect?)null;

        var area = WalkSurface.Compute(display, visibleBar, _window.PhysicalWidth);

        if (!force && area == _area) return;

        var previous = _area;
        _area = area;

        if (force)
        {
            _floor.Snap(area.FloorY);
            if (!_exitRequested) _motion.KeepInside(area);
            ApplyPosition();
            _log.Info(
                $"Walk area: x {area.Left:F0}..{area.Right:F0}, floor {area.FloorY:F0} " +
                $"(display {FormatRect(display.Bounds)}, work {FormatRect(display.UsableArea)}, " +
                $"bar {(visibleBar is { } b ? FormatRect(b) : "hidden/none")}).");
            return;
        }

        if (Math.Abs(area.FloorY - previous.FloorY) > 0.5)
        {
            // The direction of the change is the reliable signal: a shell can report
            // the bar as hit-testable for a frame while it is already sliding away.
            _log.Info(area.FloorY < previous.FloorY
                ? $"Taskbar shown: the pet steps up to floor {area.FloorY:F0}."
                : $"Taskbar hidden: the pet returns to floor {area.FloorY:F0}.");
        }

        if (area.Left != previous.Left || area.Right != previous.Right)
        {
            _log.Info($"Walk range changed to x {area.Left:F0}..{area.Right:F0}.");
            if (!_exitRequested) _motion.KeepInside(area);
            ApplyPosition();
        }
    }

    private static string FormatRect(ScreenRect rect) =>
        $"{rect.X:F0},{rect.Y:F0} {rect.Width:F0}x{rect.Height:F0}";

    private void FaceMovement() => _window.SetFacing(_motion.Facing < 0);

    private void ApplyPosition()
    {
        if (_area.FloorY <= 0) return;

        _window.MoveTo(_motion.X, _floor.Current);
    }
}
