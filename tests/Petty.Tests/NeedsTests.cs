using Petty.Core.Domain;

namespace Petty.Tests;

public class NeedsTests
{
    [Fact]
    public void DecayIsProportionalToElapsedHours()
    {
        var tuning = new PetTuning { SatietyDecayPerHour = 4, HappinessDecayPerHour = 2 };
        var needs = Needs.Full;

        var after = needs.Advance(TimeSpan.FromHours(2), tuning);

        Assert.Equal(92, after.Satiety, 3);
        Assert.Equal(96, after.Happiness, 3);
    }

    [Fact]
    public void ValuesNeverLeaveTheZeroToHundredRange()
    {
        var tuning = new PetTuning { SatietyDecayPerHour = 1000, HappinessDecayPerHour = 1000 };

        var after = Needs.Full.Advance(TimeSpan.FromDays(30), tuning);
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
        Assert.True(Needs.FromValues(20, 100).IsTired(tuning));
        Assert.False(Needs.FromValues(21, 100).IsTired(tuning));
    }
}
