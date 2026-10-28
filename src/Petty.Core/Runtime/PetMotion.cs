namespace Petty.Core.Runtime;

/// <summary>
/// Horizontal range the pet window may move through, expressed in screen pixels.
/// <see cref="Left"/> and <see cref="Right"/> are window-left limits (so the window
/// stays fully on screen); <see cref="FloorY"/> is the screen row the paws rest on.
/// </summary>
public readonly record struct MotionArea(double Left, double Right, double FloorY)
{
    public double Width => Math.Max(0, Right - Left);

    public bool HasRoom => Right > Left;
}

/// <summary>
/// Moves the pet window horizontally. It deliberately knows nothing about WPF,
/// DPI or actual window handles: the view converts <see cref="X"/> and the display
/// floor line into a window position. Every target is expressed as a window-left
/// coordinate in screen pixels.
/// </summary>
public sealed class PetMotion
{
    public const double WalkSpeed = 26;
    public const double RunSpeed = 72;
    public const double EnterSpeed = 96;
    public const double ExitSpeed = 110;
    public const double MinimumWanderDistance = 96;

    private readonly Random _random;
    private double _targetX;
    private double _speed;

    public PetMotion(double x = 0, Random? random = null)
    {
        X = x;
        _random = random ?? new Random();
    }

    /// <summary>Screen X of the window's left edge.</summary>
    public double X { get; private set; }

    /// <summary>+1 faces right, -1 faces left. The sprite is authored facing right.</summary>
    public int Facing { get; private set; } = 1;

    public bool HasTarget { get; private set; }

    public bool IsMoving => HasTarget && _speed > 0;

    public double TargetX => _targetX;

    public double Speed => _speed;

    public void Place(double x)
    {
        X = x;
        Stop();
    }

    /// <summary>Walks in from outside the left edge and stops about a third into the area.</summary>
    public void Enter(MotionArea area, double windowWidth)
    {
        X = area.Left - windowWidth;
        Facing = 1;

        var travel = Math.Clamp(area.Width * 0.3, 120, 360);
        MoveTo(area.Left + travel, EnterSpeed);
    }

    /// <summary>
    /// Share of playful (running) walks that head for the far end of the screen.
    /// Random targets alone cluster near the middle, so the pet would never be seen
    /// patrolling the whole desktop width.
    /// </summary>
    public const double EdgeTargetShare = 0.35;

    /// <summary>Longest stroll as a share of the walkable width (playful runs still cross the screen).</summary>
    public const double MaxStrollShare = 0.3;

    /// <summary>Picks a random reachable point, far enough away to be visible movement.</summary>
    public void Wander(MotionArea area, bool run)
    {
        if (!area.HasRoom)
        {
            Stop();
            return;
        }

        var desired = Math.Min(MinimumWanderDistance, area.Width / 2);
        var target = X;

        // Playful walks sometimes cross the screen from edge to edge. Short strolls
        // stay local, which keeps the pet from drifting off-screen all the time.
        if (run)
        {
            var farEdge = X < area.Left + (area.Width / 2) ? area.Right : area.Left;
            if (_random.NextDouble() < EdgeTargetShare && Math.Abs(farEdge - X) >= desired)
            {
                MoveTo(farEdge, RunSpeed);
                return;
            }
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            target = area.Left + (_random.NextDouble() * area.Width);
            if (Math.Abs(target - X) >= desired) break;
            target = X < area.Left + (area.Width / 2) ? area.Right : area.Left;
            if (Math.Abs(target - X) >= desired) break;
        }

        if (!run)
        {
            // A stroll stays within reach so the walk finishes while it still looks
            // like a stroll; crossing the whole desktop is the playful (run) case.
            var reach = Math.Max(MinimumWanderDistance, area.Width * MaxStrollShare);
            target = Math.Clamp(target, Math.Max(area.Left, X - reach), Math.Min(area.Right, X + reach));
        }

        MoveTo(target, run ? RunSpeed : WalkSpeed);
    }

    public void Seek(double targetX, double speed = WalkSpeed) => MoveTo(targetX, speed);

    /// <summary>Heads for the nearest (or the requested) screen edge and keeps going off-screen.</summary>
    public void Exit(MotionArea area, double windowWidth, int? direction = null)
    {
        var midpoint = area.Left + (area.Width / 2);
        var sign = direction ?? (X + (windowWidth / 2) >= midpoint ? 1 : -1);
        var target = sign > 0 ? area.Right + windowWidth : area.Left - windowWidth;
        MoveTo(target, ExitSpeed);
    }

    public void MoveTo(double targetX, double speed)
    {
        if (double.IsNaN(targetX) || double.IsNaN(speed) || speed <= 0)
        {
            Stop();
            return;
        }

        _targetX = targetX;
        _speed = speed;
        HasTarget = true;

        if (Math.Abs(targetX - X) > 0.01)
            Facing = targetX > X ? 1 : -1;
    }

    public void Stop()
    {
        _speed = 0;
        HasTarget = false;
    }

    /// <summary>Keeps the pet inside a changed display area without teleporting an active walk.</summary>
    public void KeepInside(MotionArea area)
    {
        var left = Math.Min(area.Left, area.Right);
        var right = Math.Max(area.Left, area.Right);

        _targetX = Math.Clamp(_targetX, left, right);
        if (!HasTarget) X = Math.Clamp(X, left, right);
    }

    /// <summary>Advances the position. Returns true when the pet actually moved.</summary>
    public bool Update(TimeSpan delta)
    {
        if (!HasTarget || delta <= TimeSpan.Zero) return false;

        var step = _speed * delta.TotalSeconds;
        var distance = _targetX - X;

        if (Math.Abs(distance) <= step)
        {
            X = _targetX;
            Stop();
        }
        else
        {
            X += Math.Sign(distance) * step;
        }

        return true;
    }
}
