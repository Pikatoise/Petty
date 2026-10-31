namespace Petty.Core.Runtime;

/// <summary>Visual food quarters and the non-queueing pour interaction.</summary>
public sealed class BowlPresentation
{
    public static readonly TimeSpan PourDuration = TimeSpan.FromSeconds(1.2);
    public bool IsPouring { get; private set; }
    private TimeSpan _elapsed;

    // Any positive amount is visible; each stage is the next whole quarter.
    public static int Level(double food, double capacity) =>
        capacity <= 0 || !double.IsFinite(food) || !double.IsFinite(capacity) || food <= 0
            ? 0 : Math.Clamp((int)Math.Ceiling(Math.Min(food, capacity) * 4 / capacity), 1, 4);

    public bool TryStart(double food, double capacity)
    {
        if (IsPouring || food >= capacity) return false;
        IsPouring = true;
        _elapsed = TimeSpan.Zero;
        return true;
    }

    public int Advance(TimeSpan delta)
    {
        if (!IsPouring) return 0;
        if (delta > TimeSpan.Zero) _elapsed += delta;
        if (_elapsed >= PourDuration) { IsPouring = false; return 5; }
        return Math.Min(5, (int)(_elapsed.TotalSeconds / PourDuration.TotalSeconds * 6));
    }

    public void Stop() => IsPouring = false;
}
