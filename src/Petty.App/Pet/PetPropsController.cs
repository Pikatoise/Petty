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
    private readonly BowlDrop _bowlDrop = new();
    private int _petWidth;
    private readonly int _contactX;
    private readonly int _contactY;
    private readonly int _groundRow;
    private readonly int _mouthX;
    private double Scale => _petWidth / 64.0;
    private double PlayContactX => _contactX * Scale;
    private double EatMouthX => _mouthX * Scale;

    public PetPropsController(PropWindow bowl, PropWindow ball, PropWindow hand, DesktopSurfaceController surface, int petWidth,
        int contactX, int contactY, int groundRow, int eatMouthX)
    {
        _bowl = bowl;
        _ball = ball;
        _hand = hand;
        _surface = surface;
        _petWidth = petWidth;
        _mouthX = eatMouthX;
        _contactX = contactX;
        _contactY = contactY;
        _groundRow = groundRow;
    }

    public void SetSpriteScale(double scale, int petWidth)
    {
        _petWidth = petWidth;
        _bowl.SetSpriteScale(scale);
        _ball.SetSpriteScale(scale);
        _hand.SetSpriteScale(scale);
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

        _bowlDrop.Stop();

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

    public bool IsBowlMoving => _bowlDrop.IsMoving;

    public void StartBowlDrop(BowlPlacementState placement)
    {
        ResolvedBowlPosition resolved = BowlPlacement.Resolve(placement, _surface.Displays, _bowl.PhysicalWidth, _bowl.PhysicalHeight);
        int top = _bowl.ScreenRect.Top;
        _bowl.PlaceAt(resolved.Left, top);
        _bowlDrop.Start(top - (Floor - _bowl.FloorOffset));
    }

    public void AdvanceBowlDrop(TimeSpan delta, TimeSpan duration)
    {
        if (_bowl.IsDragging)
        {
            _bowlDrop.Stop();
            return;
        }

        if (_bowlDrop.IsMoving)
        {
            double offset = _bowlDrop.Advance(delta, duration);
            _bowl.PlaceAt(_bowl.ScreenRect.Left, Floor - _bowl.FloorOffset + offset);
        }
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
        double mouth = fromLeft ? EatMouthX : _petWidth - EatMouthX;
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

    public double PlayContactLift => Math.Max(0, (_groundRow - _contactY) * Scale - _ball.PhysicalWidth * 0.44);

    private double BallContactCenter(double petX, int direction) =>
        petX + (direction > 0 ? PlayContactX : _petWidth - PlayContactX) + direction * _ball.PhysicalWidth * 0.44;

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
            double ratio = Scale / SpriteDisplayScale.ReferenceScale;
            _hand.PlaceAt(head - _hand.PhysicalWidth / 2.0 + direction * pose.PropOffset * ratio,
                Floor - 155 * ratio + pose.PropHeight * ratio);
        }
    }
}
