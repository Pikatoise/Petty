using Petty.Core.Domain;

namespace Petty.Tests;

public class NeedsTests
{
    [Fact]
    public void DecayRatesAreMeasuredPerMinute()
    {
        var tuning = new PetTuning { SatietyDecayPerMinute = 2, HappinessDecayPerMinute = 1 };
        Needs after = Needs.Full.Advance(TimeSpan.FromMinutes(3), tuning);
        Assert.Equal(94, after.Satiety, 3);
        Assert.Equal(97, after.Happiness, 3);
        Assert.Equal(85, Needs.Full.Advance(TimeSpan.FromMinutes(60), PetTuning.Default).Satiety, 3);
    }

    [Fact]
    public void ValuesNeverLeaveTheZeroToHundredRange()
    {
        var tuning = new PetTuning { SatietyDecayPerMinute = 1000.0 / 60, HappinessDecayPerMinute = 1000.0 / 60 };

        Needs after = Needs.Full.Advance(TimeSpan.FromDays(30), tuning);
        Assert.Equal(0, after.Satiety);
        Assert.Equal(0, after.Happiness);

        var overfed = Needs.FromValues(200, -50);
        Assert.Equal(100, overfed.Satiety);
        Assert.Equal(0, overfed.Happiness);

        Assert.Equal(100, Needs.FromValues(double.NaN, double.PositiveInfinity).Happiness);
        Assert.Equal(0, Needs.FromValues(double.NaN, 50).Satiety);
    }

    [Fact]
    public void NegativeElapsedTimeIsIgnored()
    {
        var needs = Needs.FromValues(50, 50);
        Assert.Equal(needs, needs.Advance(TimeSpan.FromMinutes(-30), PetTuning.Default));
    }

    [Fact]
    public void HungerAndTirednessThresholdsAreRespected()
    {
        var tuning = new PetTuning { HungryThreshold = 30, TiredThreshold = 20 };

        Assert.True(Needs.FromValues(30, 100).IsHungry(tuning));
        Assert.False(Needs.FromValues(31, 100).IsHungry(tuning));
        Assert.True(Needs.FromValues(20, 100).IsStarving(tuning));
        Assert.False(Needs.FromValues(21, 100).IsStarving(tuning));
    }
}
