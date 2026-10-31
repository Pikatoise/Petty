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
#if DEBUG
    [Fact]
    public void ScaledClockChangesSpeedWithoutJumpsAndPausesDuringSleep()
    {
        var clock = new ActiveClock(TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(1), clock.ReadScaled(TimeSpan.FromSeconds(1)));
        clock.SetSpeed(4, TimeSpan.FromSeconds(1.5));
        Assert.Equal(TimeSpan.FromSeconds(3.5), clock.ReadScaled(TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.FromSeconds(2), clock.Read(TimeSpan.FromSeconds(2)));
        clock.SetSpeed(0.5, TimeSpan.FromSeconds(2));
        clock.Suspend(TimeSpan.FromSeconds(3));
        clock.Resume(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromSeconds(4.5), clock.ReadScaled(TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1))));
        Assert.Equal(TimeSpan.FromSeconds(4), clock.Read(TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetSpeed(double.NaN, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void ScaledTimeAdvancesNeedsAndActionsTogether()
    {
        var clock = new ActiveClock(TimeSpan.Zero);
        clock.SetSpeed(10, TimeSpan.Zero);
        var settings = new Petty.Core.Diagnostics.DebugTuning { SatietyDecayPerMinute = 1, HappinessDecayPerMinute = 0 };
        var runtime = PetRuntime.CreateNew("Cat", settings.ToPetTuning(), DateTimeOffset.UtcNow);
        runtime.SetNeeds(80, 50);
        Assert.True(runtime.RequestPet());
        var previous = clock.ReadScaled(TimeSpan.Zero);
        var current = clock.ReadScaled(TimeSpan.FromSeconds(0.3));
        runtime.Update(current - previous, DateTimeOffset.UtcNow);
        Assert.Equal(79.95, runtime.Needs.Satiety, 4);
        Assert.Equal(62, runtime.Needs.Happiness, 4);
        Assert.Equal(Petty.Core.Behavior.PetActivity.Idle, runtime.Behavior.State);
    }
#endif

}
