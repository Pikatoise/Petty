using Petty.Core.Behavior;
using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.Tests;

public class PetRuntimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    private static PetTuning Tuning() => new()
    {
        SatietyDecayPerHour = 0,
        HappinessDecayPerHour = 0,
        HungryThreshold = 30,
        TiredThreshold = 20,
        SatietyRecoveryThreshold = 45,
        BowlCapacity = 100,
        BowlFoodPerClick = 25,
        EatFoodPerBite = 5,
        EatSatietyPerBite = 10,
        EatBiteInterval = TimeSpan.FromSeconds(1),
        MaxEatDuration = TimeSpan.FromMinutes(1),
        PlayDuration = TimeSpan.FromSeconds(10),
        PlayHappinessGain = 30,
        PlaySatietyCost = 5,
        WanderMinInterval = TimeSpan.FromHours(1),
        WanderMaxInterval = TimeSpan.FromHours(1),
        SleepAfterIdle = TimeSpan.FromHours(1),
        ExitDuration = TimeSpan.FromSeconds(2),
    };

    private static PetRuntime CreateRuntime(double satiety = 25, double happiness = 50, double bowlFood = 0, PetTuning? tuning = null) =>
        PetRuntime.Restore(
            new PetSnapshot
            {
                PetName = "Muffin",
                Satiety = satiety,
                Happiness = happiness,
                BowlFood = bowlFood,
                CreatedUtc = Now.AddDays(-2),
                LastUpdatedUtc = Now,
            },
            tuning ?? Tuning(),
            Now,
            new Random(1));

    [Fact]
    public void CreateNewUsesCatalogDefaultsAndValidatesName()
    {
        var runtime = PetRuntime.CreateNew("  Biscuit ", Tuning(), Now);

        Assert.Equal("Biscuit", runtime.Snapshot.PetName);
        Assert.Equal(PetCatalog.DefaultBreedId, runtime.Snapshot.BreedId);
        Assert.Equal(PetCatalog.DefaultCoatId, runtime.Snapshot.CoatId);
        Assert.Equal(Needs.Maximum, runtime.Needs.Satiety);
        Assert.Throws<ArgumentException>(() => PetRuntime.CreateNew("   ", Tuning(), Now));
    }

    [Fact]
    public void EatingConsumesBowlAndRaisesSatiety()
    {
        var runtime = CreateRuntime(bowlFood: 20);

        runtime.Update(TimeSpan.FromMilliseconds(200), Now);
        Assert.Equal(PetActivity.SeekingFood, runtime.Behavior.State);

        runtime.NotifyArrived();
        Assert.Equal(PetActivity.Eating, runtime.Behavior.State);

        runtime.Update(TimeSpan.FromSeconds(1), Now);
        Assert.Equal(15, runtime.Bowl.Food, 3);
        Assert.Equal(35, runtime.Needs.Satiety, 3);
    }

    [Fact]
    public void EatingStopsWhenBowlIsEmpty()
    {
        var runtime = CreateRuntime(bowlFood: 10);
        runtime.Update(TimeSpan.FromMilliseconds(200), Now);
        runtime.NotifyArrived();

        for (var i = 0; i < 10 && runtime.Bowl.HasFood; i++)
            runtime.Update(TimeSpan.FromSeconds(1), Now);

        Assert.Equal(0, runtime.Bowl.Food, 3);
        Assert.Equal(PetActivity.Idle, runtime.Behavior.State);
        Assert.True(runtime.Needs.Satiety > 25);
    }

    [Fact]
    public void PlayingRaisesHappinessOverTime()
    {
        var runtime = CreateRuntime(satiety: 80, happiness: 20);

        Assert.True(runtime.RequestPlay());
        runtime.Update(TimeSpan.FromSeconds(5), Now);

        Assert.Equal(35, runtime.Needs.Happiness, 3);
        Assert.Equal(77.5, runtime.Needs.Satiety, 3);
        Assert.Equal(PetActivity.Playing, runtime.Behavior.State);
    }

    [Fact]
    public void PlayFinishesAndRestoresIdle()
    {
        var runtime = CreateRuntime(satiety: 80, happiness: 20);
        runtime.RequestPlay();

        runtime.Update(TimeSpan.FromSeconds(11), Now);

        Assert.Equal(PetActivity.Idle, runtime.Behavior.State);
    }

    [Fact]
    public void RefillBowlAddsAFixedPortionAndRespectsCapacity()
    {
        var runtime = CreateRuntime();

        Assert.Equal(25, runtime.RefillBowl());
        Assert.Equal(25, runtime.RefillBowl());
        Assert.Equal(25, runtime.RefillBowl());
        Assert.Equal(25, runtime.RefillBowl());
        Assert.Equal(0, runtime.RefillBowl());
        Assert.Equal(100, runtime.Bowl.Food, 3);
    }

    [Fact]
    public void ExitIsReportedCompleteOnlyAfterAnimationDuration()
    {
        var runtime = CreateRuntime();
        Assert.True(runtime.RequestExit());
        Assert.False(runtime.IsExitAnimationComplete);

        runtime.Update(TimeSpan.FromSeconds(2.1), Now);
        Assert.True(runtime.IsExitAnimationComplete);
    }

    [Fact]
    public void SnapshotCarriesLiveValuesAndPlacement()
    {
        var runtime = CreateRuntime(bowlFood: 40);
        runtime.Update(TimeSpan.FromMinutes(1), Now);
        runtime.UpdateBowlPlacement(new BowlPlacementState(0.4, 90, "DISPLAY1"));

        var snapshot = runtime.ToSnapshot();

        Assert.Equal(runtime.Needs.Satiety, snapshot.Satiety, 3);
        Assert.Equal(runtime.Needs.Happiness, snapshot.Happiness, 3);
        Assert.Equal(40, snapshot.BowlFood, 3);
        Assert.Equal(0.4, snapshot.BowlRelativeX, 3);
        Assert.Equal(90, snapshot.BowlBottomOffset, 3);
        Assert.Equal("DISPLAY1", snapshot.DisplayDeviceName);
        Assert.Equal(PetSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
    }

    [Fact]
    public void OneHourActiveUpdateDecaysNeeds()
    {
        var tuning = new PetTuning { SatietyDecayPerHour = 60, HappinessDecayPerHour = 0 };
        var runtime = CreateRuntime(satiety: 100, happiness: 100, tuning: tuning);

        runtime.Update(TimeSpan.FromHours(1), Now);

        Assert.Equal(40, runtime.Needs.Satiety, 3);
    }

    [Fact]
    public void UserInteractionWakesUpSleepingPet()
    {
        var tuning = Tuning();
        var runtime = CreateRuntime(satiety: 90, happiness: 90, tuning: tuning);

        for (var i = 0; i < 12; i++)
            runtime.Update(TimeSpan.FromMinutes(5), Now);

        Assert.Equal(PetActivity.Sleeping, runtime.Behavior.State);

        runtime.OnUserInteraction();
        Assert.Equal(PetActivity.Idle, runtime.Behavior.State);
    }
}
