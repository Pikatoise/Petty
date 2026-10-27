namespace Petty.Core.Runtime;

/// <summary>
/// Eases the ground line the pet stands on. When the taskbar slides in or out the
/// target floor changes instantly, but the pet should step up or down instead of
/// teleporting, so the window is moved at a fixed vertical speed.
/// </summary>
public sealed class FloorFollow
{
    /// <summary>How fast the pet climbs onto or off the taskbar, in screen pixels per second.</summary>
    public const double PixelsPerSecond = 900;

    private const double MaxStep = 0.5;

    public FloorFollow(double floor = 0) => Current = floor;

    /// <summary>Ground row the pet's paws are currently resting on.</summary>
    public double Current { get; private set; }

    public bool IsAt(double target, double tolerance = 0.5) => Math.Abs(target - Current) <= tolerance;

    /// <summary>Moves towards <paramref name="target"/>; returns the new ground row.</summary>
    public double Advance(double target, TimeSpan delta, double pixelsPerSecond = PixelsPerSecond)
    {
        if (double.IsNaN(target) || double.IsInfinity(target)) return Current;
        if (delta <= TimeSpan.Zero) return Current;

        if (double.IsNaN(Current) || double.IsInfinity(Current) || pixelsPerSecond <= 0)
        {
            Current = target;
            return Current;
        }

        var step = pixelsPerSecond * Math.Min(delta.TotalSeconds, MaxStep);
        var distance = target - Current;

        Current = Math.Abs(distance) <= step ? target : Current + (Math.Sign(distance) * step);
        return Current;
    }

    /// <summary>Jumps to a floor without easing (startup, display changes).</summary>
    public void Snap(double floor)
    {
        if (double.IsNaN(floor) || double.IsInfinity(floor)) return;
        Current = floor;
    }
}
