using Petty.App.Desktop;
using Petty.App.Views;
using Petty.Core.Domain;

namespace Petty.App.Pet;

/// <summary>Positions the bowl and play ball on the current desktop surface.</summary>
internal sealed class PetPropsController
{
    private readonly PropWindow _bowl;
    private readonly PropWindow _ball;
    private readonly DesktopSurfaceController _surface;
    private readonly int _petWidth;

    public PetPropsController(PropWindow bowl, PropWindow ball, DesktopSurfaceController surface, int petWidth)
    {
        _bowl = bowl;
        _ball = ball;
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
    public void ShowBowlFilled(bool filled) => _bowl.ShowFilled(filled);

    public void UpdateBall(double petX, TimeSpan elapsed)
    {
        var area = _surface.WalkArea;
        if (area.FloorY <= 0) return;
        var center = petX + (_petWidth / 2.0);
        var amplitude = Math.Clamp(area.Width * 0.06, 48, 90);
        var swing = Math.Sin(elapsed.TotalSeconds * 2.2);
        var left = Math.Clamp(center + (swing * amplitude) - (_ball.PhysicalWidth / 2.0), area.Left, area.Right);
        _ball.PlaceAt(left, Floor - _ball.PhysicalHeight - (Math.Abs(swing) * 26));
    }
}
