using Petty.App.Desktop;
using Petty.App.Views;
using Petty.Core.Domain;
using Petty.Core.Behavior;
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

    public PetPropsController(PropWindow bowl, PropWindow ball, PropWindow hand, DesktopSurfaceController surface, int petWidth)
    {
        _bowl = bowl;
        _ball = ball;
        _hand = hand;
        _surface = surface;
        _petWidth = petWidth;
    }

    private double Floor => _surface.FloorY > 0 ? _surface.FloorY : _surface.WalkArea.FloorY;

    public void ApplyBowlPosition(BowlPlacementState placement)
    {
        if (_surface.PrimaryDisplay is { } display) _bowl.DragBounds = display.UsableArea;
        if (_bowl.Handle == IntPtr.Zero || _bowl.IsDragging) return;
        var resolved = BowlPlacement.Resolve(placement, _surface.Displays, _bowl.PhysicalWidth, _bowl.PhysicalHeight);
        if (Floor <= 0) _bowl.PlaceAt(resolved.Left, resolved.Top);
        else _bowl.PlaceOnFloor(resolved.Left, Floor);
    }

    public BowlPlacementState CaptureBowlPosition()
    {
        var rect = _bowl.ScreenRect;
        return BowlPlacement.Capture(rect.Left, Floor - rect.Height, rect.Width, rect.Height, _surface.Displays);
    }

    public double SeekTargetX(double petX)
    {
        var area = _surface.WalkArea;
        var rect = _bowl.ScreenRect;
        if (rect.Width <= 0) return area.Left + (area.Width * 0.55);
        var center = rect.Left + (rect.Width / 2.0);
        var fromLeft = petX + (_petWidth / 2.0) < center;
        var head = _petWidth * (fromLeft ? 0.68 : 0.32);
        return Math.Clamp(center - head, area.Left, area.Right);
    }

    public void ShowBall() => _ball.Show();
    public void HideBall() => _ball.Hide();

    public void HideScenes() { _ball.Hide(); _hand.Hide(); }
    public void ShowHand() => _hand.Show();

    public void UpdateScene(PetActivity activity, double petX, int direction, double progress)
    {
        var pose = ActionScene.Sample(activity, progress);
        if (activity is PetActivity.Playing)
        {
            var head = petX + (direction > 0 ? _petWidth * 0.82 : _petWidth * 0.18);
            var center = head + direction * pose.PropOffset;
            _ball.PlaceOnFloor(center - _ball.PhysicalWidth / 2.0, Floor);
        }
        else if (activity is PetActivity.Petting)
        {
            var head = petX + (direction > 0 ? _petWidth * 0.68 : _petWidth * 0.32);
            _hand.PlaceAt(head - _hand.PhysicalWidth / 2.0 + direction * pose.PropOffset,
                Floor - 155 + pose.PropHeight);
        }
    }
}
