using Petty.App.Desktop;
using Petty.App.Views;
using Petty.Core.Behavior;
using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.App.Pet;

/// <summary>Positions the bowl and play ball on the current desktop surface.</summary>
internal sealed class PetPropsController
{
    private readonly PropWindow _bowl;
    private readonly PropWindow _ball;
    private readonly PropWindow _hand;
    private readonly DesktopSurfaceController _surface;
    private readonly int _petWidth;
    private readonly double _playContactX;
    private readonly double _playContactLift;
    private readonly double _eatMouthX;

    public PetPropsController(PropWindow bowl, PropWindow ball, PropWindow hand, DesktopSurfaceController surface, int petWidth,
        int contactX, int contactY, int groundRow, int eatMouthX)
    {
        _bowl = bowl;
        _ball = ball;
        _hand = hand;
        _surface = surface;
        _petWidth = petWidth;
        double scale = petWidth / 64.0;
        _eatMouthX = eatMouthX * scale;
        _playContactX = contactX * scale;
        _playContactLift = Math.Max(0, (groundRow - contactY) * scale - ball.PhysicalWidth * 0.44);
    }

    private double Floor => _surface.FloorY > 0 ? _surface.FloorY : _surface.WalkArea.FloorY;

    public void ApplyBowlPosition(BowlPlacementState placement)
    {
        if (_surface.PrimaryDisplay is { } display)
        {
            _bowl.DragBounds = display.UsableArea;
        }

        if (_bowl.Handle == IntPtr.Zero || _bowl.IsDragging)
        {
            return;
        }

        ResolvedBowlPosition resolved = BowlPlacement.Resolve(placement, _surface.Displays, _bowl.PhysicalWidth, _bowl.PhysicalHeight);
        if (Floor <= 0)
        {
            _bowl.PlaceAt(resolved.Left, resolved.Top);
        }
        else
        {
            _bowl.PlaceOnFloor(resolved.Left, Floor);
        }
    }

    public BowlPlacementState CaptureBowlPosition()
    {
        (int Left, int Top, int Width, int Height) rect = _bowl.ScreenRect;
        return BowlPlacement.Capture(rect.Left, Floor - rect.Height, rect.Width, rect.Height, _surface.Displays);
    }

    public double SeekTargetX(double petX)
    {
        MotionArea area = _surface.WalkArea;
        (int Left, int Top, int Width, int Height) rect = _bowl.ScreenRect;
        if (rect.Width <= 0)
        {
            return area.Left + (area.Width * 0.55);
        }

        double center = rect.Left + (rect.Width / 2.0);
        bool fromLeft = petX + (_petWidth / 2.0) < center;
        // Align the eating mouth, not the old standing-head anchor. Mirror the
        // same sprite coordinate when approaching the bowl from its right.
        double mouth = fromLeft ? _eatMouthX : _petWidth - _eatMouthX;
        return Math.Clamp(center - mouth, area.Left, area.Right);
    }

    public bool IsBowlLeft(double petX)
    {
        (int Left, int Top, int Width, int Height) rect = _bowl.ScreenRect;
        return rect.Left + rect.Width / 2.0 < petX + _petWidth / 2.0;
    }

    public void ShowBall()
    {
        _ball.SetRotation(0);
        _ball.Show();
    }
    public void HideBall() => _ball.Hide();

    public void HideScenes()
    {
        _ball.Hide();
        _hand.Hide();
    }
    public void ShowHand() => _hand.Show();

    public double PlayContactLift => _playContactLift;

    private double BallContactCenter(double petX, int direction) =>
        petX + (direction > 0 ? _playContactX : _petWidth - _playContactX) + direction * _ball.PhysicalWidth * 0.44;

    public double PlayExitDistance(double petX, int direction)
    {
        double center = BallContactCenter(petX, direction);
        double left = _surface.PrimaryDisplay?.Bounds.X ?? _surface.WalkArea.Left;
        double right = _surface.PrimaryDisplay?.Bounds.Right ?? _surface.WalkArea.Right + _petWidth;
        return Math.Max(120, (direction > 0 ? right - center : center - left) + _ball.PhysicalWidth);
    }

    public void UpdatePlay(double petX, int direction, PlayScenePose pose)
    {
        double center = BallContactCenter(petX, direction) + direction * pose.PropOffset;
        _ball.PlaceOnFloor(center - _ball.PhysicalWidth / 2.0, Floor + pose.PropHeight);
        double radius = _ball.PhysicalWidth * 0.44;
        _ball.SetRotation(direction * pose.PropOffset / radius * 180 / Math.PI);
    }

    public void UpdateScene(PetActivity activity, double petX, int direction, double progress)
    {
        (double PropOffset, double PropHeight) pose = ActionScene.Sample(activity, progress);
        if (activity is PetActivity.Petting)
        {
            double head = petX + (direction > 0 ? _petWidth * 0.68 : _petWidth * 0.32);
            _hand.PlaceAt(head - _hand.PhysicalWidth / 2.0 + direction * pose.PropOffset,
                Floor - 155 + pose.PropHeight);
        }
    }
}
