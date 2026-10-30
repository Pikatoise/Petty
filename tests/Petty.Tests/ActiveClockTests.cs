using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.Tests;

public class ActiveClockTests
{
    [Fact]
    public void RestoreKeepsNeedsFromOldSave()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new PetSnapshot { PetName = "Muffin", Satiety = 60, Happiness = 80,
            BowlFood = 30, LastUpdatedUtc = now.AddDays(-30) };

        var runtime = PetRuntime.Restore(snapshot, PetTuning.Default, now);

        Assert.Equal(60, runtime.Needs.Satiety);
        Assert.Equal(80, runtime.Needs.Happiness);
        Assert.Equal(30, runtime.Bowl.Food);
        Assert.Equal(now, runtime.ToSnapshot().LastUpdatedUtc);
    }

    [Fact]
    public void AwakeTimeAccumulatesForOneHour()
    {
        var clock = new ActiveClock(TimeSpan.Zero);
        var previous = TimeSpan.Zero;
        var needs = Needs.Full;
        for (var second = 1; second <= 3600; second++)
        {
            var elapsed = clock.Read(TimeSpan.FromSeconds(second));
            needs = needs.Advance(elapsed - previous, PetTuning.Default);
            previous = elapsed;
        }

        Assert.Equal(96, needs.Satiety, 6);
        Assert.Equal(97, needs.Happiness, 6);
    }

    [Fact]
    public void SuspendResumeAndUnexpectedGapsDoNotAdvanceTime()
    {
        var clock = new ActiveClock(TimeSpan.Zero);
        clock.Suspend(TimeSpan.FromSeconds(1));
        clock.Suspend(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(1), clock.Read(TimeSpan.FromHours(2)));
        clock.Resume(TimeSpan.FromHours(2));
        clock.Resume(TimeSpan.FromHours(2));
        Assert.Equal(TimeSpan.FromSeconds(2), clock.Read(TimeSpan.FromHours(2).Add(TimeSpan.FromSeconds(1))));
        Assert.Equal(TimeSpan.FromSeconds(2), clock.Read(TimeSpan.FromHours(3)));
        Assert.Equal(TimeSpan.FromSeconds(3), clock.Read(TimeSpan.FromHours(3).Add(TimeSpan.FromSeconds(1))));
    }
}
