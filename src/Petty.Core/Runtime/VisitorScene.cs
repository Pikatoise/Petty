using Petty.Core.Domain;

namespace Petty.Core.Runtime;

public enum VisitorPhase
{
    Entering, Sitting, Waiting, Exiting, Finished
}

/// <summary>A visitor's movement and poses, independent of needs, networking and windows.</summary>
public sealed class VisitorScene
{
    private readonly PetMotion _motion = new();
    private readonly TimeSpan _sitDuration;
    private readonly TimeSpan _riseDuration;
    private MotionArea _area;
    private ScreenRect _screen;
    private double _width;
    private double _restX;
    private TimeSpan _phaseTime;
    private bool _rising;
    private bool _exitRight;

    public VisitorScene(MotionArea area, ScreenRect screen, double width, IReadOnlyList<ScreenRect> obstacles,
        TimeSpan sitDuration, TimeSpan riseDuration)
    {
        _area = area;
        _screen = screen;
        _width = width;
        _sitDuration = sitDuration;
        _riseDuration = riseDuration;
        _restX = RestingX(area, width, obstacles);
        bool fromRight = _restX > (area.Left + area.Right) / 2;
        _motion.Place(fromRight ? screen.Right : screen.X - width);
        _motion.MoveTo(_restX, PetMotion.EnterSpeed);
    }

    public VisitorPhase Phase { get; private set; } = VisitorPhase.Entering;
    public double X => _motion.X;
    public bool FaceLeft => _motion.Facing < 0;
    public string Clip => Phase switch
    {
        VisitorPhase.Sitting => "mail-sit",
        VisitorPhase.Waiting => "mail-idle",
        VisitorPhase.Exiting when _rising => "mail-exit",
        _ => "mail-walk"
    };

    public void Advance(TimeSpan delta)
    {
        if (delta <= TimeSpan.Zero || Phase is VisitorPhase.Finished)
        {
            return;
        }
        _phaseTime += delta;
        if (Phase is VisitorPhase.Exiting && _rising)
        {
            if (_phaseTime < _riseDuration)
            {
                return;
            }
            delta = _phaseTime - _riseDuration;
            _rising = false;
            ExitMotion();
        }
        _motion.Update(delta);
        if (Phase is VisitorPhase.Entering && !_motion.IsMoving)
        {
            Phase = VisitorPhase.Sitting;
            _phaseTime = TimeSpan.Zero;
        }
        else if (Phase is VisitorPhase.Sitting && _phaseTime >= _sitDuration)
        {
            Phase = VisitorPhase.Waiting;
        }
        else if (Phase is VisitorPhase.Exiting && !_motion.IsMoving)
        {
            Phase = VisitorPhase.Finished;
        }
    }

    public void RequestExit()
    {
        if (Phase is VisitorPhase.Exiting or VisitorPhase.Finished)
        {
            return;
        }
        _exitRight = X + _width / 2 > _screen.X + _screen.Width / 2;
        _rising = Phase is VisitorPhase.Waiting or VisitorPhase.Sitting;
        Phase = VisitorPhase.Exiting;
        _phaseTime = TimeSpan.Zero;
        _motion.Stop();
        if (!_rising)
        {
            ExitMotion();
        }
    }

    public void UpdateSurface(MotionArea area, ScreenRect screen, double width, IReadOnlyList<ScreenRect> obstacles)
    {
        bool displayChanged = screen != _screen || width != _width;
        double relativeCenter = (X + _width / 2 - _screen.X) / _screen.Width;
        _area = area;
        _screen = screen;
        _width = width;
        if (Phase is VisitorPhase.Finished)
        {
            return;
        }
        if (displayChanged)
        {
            // Preserve partial entry/exit instead of pulling an off-screen guest back inside.
            _motion.Place(screen.X + relativeCenter * screen.Width - width / 2);
            if (Phase is VisitorPhase.Waiting or VisitorPhase.Sitting)
            {
                _motion.KeepInside(area);
            }
        }
        if (Phase is VisitorPhase.Exiting)
        {
            if (!_rising)
            {
                ExitMotion();
            }
            return;
        }
        double rest = RestingX(area, width, obstacles);
        if (Math.Abs(rest - _restX) > 1 || displayChanged)
        {
            _restX = rest;
            _motion.MoveTo(rest, PetMotion.WalkSpeed);
            Phase = VisitorPhase.Entering;
            _phaseTime = TimeSpan.Zero;
        }
    }

    private void ExitMotion() => _motion.Exit(_area, _width, _exitRight ? 1 : -1, _screen.X, _screen.Right);

    /// <summary>Choose an edge with room for the guest and the local bowl/menu.</summary>
    private static double RestingX(MotionArea area, double width, IReadOnlyList<ScreenRect> obstacles)
    {
        double margin = Math.Min(width / 3, area.Width / 4);
        double left = area.Left + margin;
        double right = area.Right - margin;
        double Clearance(double x) => obstacles.Count == 0 ? 0 : obstacles.Min(o =>
            Math.Max(o.X - (x + width), x - o.Right));
        return Clearance(right) > Clearance(left) ? right : left;
    }
}
