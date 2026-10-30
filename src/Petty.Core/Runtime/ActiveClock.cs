namespace Petty.Core.Runtime;

/// <summary>Accumulates monotonic awake time shared by simulation and animation.</summary>
public sealed class ActiveClock(TimeSpan initialTimestamp)
{
    private static readonly TimeSpan MaxObservedGap = TimeSpan.FromSeconds(5);
    private readonly object _gate = new();
    private TimeSpan _last = initialTimestamp;
    private TimeSpan _total;
    private bool _suspended;

    public TimeSpan Read(TimeSpan timestamp)
    {
        lock (_gate) return Advance(timestamp);
    }

    public void Suspend(TimeSpan timestamp)
    {
        lock (_gate)
        {
            Advance(timestamp);
            _suspended = true;
        }
    }

    public void Resume(TimeSpan timestamp)
    {
        lock (_gate)
        {
            if (!_suspended) return;
            _last = timestamp;
            _suspended = false;
        }
    }

    private TimeSpan Advance(TimeSpan timestamp)
    {
        var delta = timestamp - _last;
        if (delta < TimeSpan.Zero) return _total;
        _last = timestamp;
        if (!_suspended && delta > TimeSpan.Zero && delta <= MaxObservedGap)
            _total += delta;
        return _total;
    }
}
