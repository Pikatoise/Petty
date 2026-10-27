using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.Tests;

public class OfflineProgressionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ElapsedTimeDecaysNeeds()
    {
        var tuning = new PetTuning { SatietyDecayPerHour = 5, HappinessDecayPerHour = 1 };
        var result = OfflineProgression.Apply(Needs.Full, Now.AddHours(-4), Now, tuning);

        Assert.Equal(80, result.Needs.Satiety, 3);
        Assert.Equal(96, result.Needs.Happiness, 3);
        Assert.Equal(TimeSpan.FromHours(4), result.AppliedElapsed);
        Assert.False(result.ClockWentBackwards);
    }

    [Fact]
    public void LongAbsenceIsClampedToMaxOfflineSpan()
    {
        var tuning = new PetTuning { SatietyDecayPerHour = 10, MaxOfflineSpan = TimeSpan.FromHours(1) };
        var result = OfflineProgression.Apply(Needs.Full, Now.AddDays(-30), Now, tuning);

        Assert.Equal(90, result.Needs.Satiety, 3);
        Assert.Equal(TimeSpan.FromHours(1), result.AppliedElapsed);
    }

    [Fact]
    public void ClockGoingBackwardsDoesNotChangeNeeds()
    {
        var result = OfflineProgression.Apply(Needs.FromValues(50, 50), Now.AddHours(2), Now, PetTuning.Default);

        Assert.Equal(50, result.Needs.Satiety);
        Assert.Equal(50, result.Needs.Happiness);
        Assert.True(result.ClockWentBackwards);
        Assert.Equal(TimeSpan.Zero, result.AppliedElapsed);
    }

    [Fact]
    public void MissingTimestampIsTreatedAsNoElapsedTime()
    {
        var result = OfflineProgression.Apply(Needs.FromValues(70, 70), default, Now, PetTuning.Default);

        Assert.Equal(70, result.Needs.Satiety);
        Assert.Equal(TimeSpan.Zero, result.AppliedElapsed);
    }

    [Fact]
    public void ResultNeverDropsBelowZero()
    {
        var tuning = new PetTuning { SatietyDecayPerHour = 1000, HappinessDecayPerHour = 1000 };
        var result = OfflineProgression.Apply(Needs.Full, Now.AddDays(-30), Now, tuning);

        Assert.Equal(0, result.Needs.Satiety);
        Assert.Equal(0, result.Needs.Happiness);
    }

    [Fact]
    public void RuntimeRestoreAppliesOfflineProgress()
    {
        var snapshot = new PetSnapshot
        {
            PetName = "Muffin",
            Satiety = 100,
            Happiness = 100,
            BowlFood = 30,
            LastUpdatedUtc = Now.AddHours(-10),
            CreatedUtc = Now.AddDays(-1),
        };
        var tuning = new PetTuning { SatietyDecayPerHour = 4, HappinessDecayPerHour = 2 };

        var runtime = PetRuntime.Restore(snapshot, tuning, Now);

        Assert.Equal(60, runtime.Needs.Satiety, 3);
        Assert.Equal(80, runtime.Needs.Happiness, 3);
        Assert.Equal(30, runtime.Bowl.Food);
        Assert.Equal(Now, runtime.LastUpdatedUtc);
    }
}
