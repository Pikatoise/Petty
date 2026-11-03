namespace Petty.Core.Runtime;

/// <summary>Eases a released bowl towards the shared floor without crossing it.</summary>
public sealed class BowlDrop
{
    private double _startOffset;
    private double _progress = 1;

    public bool IsMoving => _progress < 1;

    public void Start(double offset)
    {
        _startOffset = offset;
        _progress = Math.Abs(offset) < 0.5 ? 1 : 0;
    }

    public double Advance(TimeSpan delta, TimeSpan duration)
    {
        if (delta > TimeSpan.Zero)
        {
            _progress = duration > TimeSpan.Zero
                ? Math.Min(1, _progress + delta.TotalSeconds / duration.TotalSeconds)
                : 1;
        }

        double eased = _progress * _progress * (3 - 2 * _progress);
        return _startOffset * (1 - eased);
    }

    public void Stop() => _progress = 1;
}
