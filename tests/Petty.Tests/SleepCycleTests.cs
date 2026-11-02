using Petty.Core.Behavior;
using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.Tests;

public class SleepCycleTests
{
    [Fact]
    public void AwakeAndSleepDurationsAreSampledOnceAndResampledForNextCycle()
    {
        var random = new ControlledRandom();
        var machine = new PetBehaviorMachine(new PetTuning
        {
            WanderMinInterval = TimeSpan.FromHours(1),
            WanderMaxInterval = TimeSpan.FromHours(1),
        }, random);
        var bowl = new Bowl(0, 100);
        random.Value = 0.5;
        machine.Update(TimeSpan.FromSeconds(359), Needs.Full, bowl);
        Assert.Equal(PetActivity.Idle, machine.State);
        machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
        random.Value = 0;
        machine.Update(TimeSpan.FromSeconds(479), Needs.Full, bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
        random.Value = 0.5;
        machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
        Assert.Equal(PetActivity.Idle, machine.State);
        random.Value = 0;
        machine.Update(TimeSpan.FromSeconds(479), Needs.Full, bowl);
        Assert.Equal(PetActivity.Idle, machine.State);
        machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
        machine.Update(TimeSpan.FromSeconds(360), Needs.Full, bowl);
        Assert.Equal(PetActivity.Idle, machine.State);
    }

    private sealed class ControlledRandom : Random
    {
        public double Value
        {
            get; set;
        }
        public override double NextDouble() => Value;
    }

    [Fact]
    public void AwakeInteractionFinishesBeforeOverdueSleepStarts()
    {
        var machine = new PetBehaviorMachine(Tuning());
        var bowl = new Bowl(0, 100);
        machine.Update(TimeSpan.FromSeconds(9), Needs.Full, bowl);
        Assert.True(machine.RequestPlay());
        machine.Update(TimeSpan.FromSeconds(5), Needs.Full, bowl);
        Assert.Equal(PetActivity.Playing, machine.State);
        machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
        Assert.Equal(PetActivity.Idle, machine.State);
        machine.Update(TimeSpan.FromMilliseconds(200), Needs.Full, bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
    }

    private static PetTuning Tuning() => new()
    {
        AwakeMinDuration = TimeSpan.FromSeconds(10),
        AwakeMaxDuration = TimeSpan.FromSeconds(10),
        SleepMinDuration = TimeSpan.FromSeconds(20),
        SleepMaxDuration = TimeSpan.FromSeconds(20),
        WanderMinInterval = TimeSpan.FromSeconds(1),
        WanderMaxInterval = TimeSpan.FromSeconds(1),
        RunChance = 1,
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MovementCountsTowardSleepAndWakingStartsFreshCycle(double runChance)
    {
        var machine = new PetBehaviorMachine(new PetTuning
        {
            AwakeMinDuration = TimeSpan.FromSeconds(10),
            AwakeMaxDuration = TimeSpan.FromSeconds(10),
            SleepMinDuration = TimeSpan.FromSeconds(20),
            SleepMaxDuration = TimeSpan.FromSeconds(20),
            WanderMinInterval = TimeSpan.FromSeconds(1),
            WanderMaxInterval = TimeSpan.FromSeconds(1),
            RunChance = runChance,
        });
        var bowl = new Bowl(0, 100);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
            Assert.Equal(runChance == 1 ? PetActivity.Running : PetActivity.Walking, machine.State);
            machine.Update(TimeSpan.FromSeconds(8), Needs.Full, bowl);
            Assert.NotEqual(PetActivity.Sleeping, machine.State);
            machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
            Assert.Equal(PetActivity.Sleeping, machine.State);
            machine.Update(TimeSpan.FromSeconds(19), Needs.Full, bowl);
            Assert.Equal(PetActivity.Sleeping, machine.State);
            machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
            Assert.Equal(PetActivity.Idle, machine.State);
        }
    }

    [Fact]
    public void RepeatedPettingPausesRemainingSleepAndPlayIsBlocked()
    {
        var machine = new PetBehaviorMachine(Tuning());
        var bowl = new Bowl(0, 100);
        machine.Update(TimeSpan.FromSeconds(10), Needs.Full, bowl);
        for (int i = 0; i < 2; i++)
        {
            machine.Update(TimeSpan.FromSeconds(5), Needs.Full, bowl);
            machine.OnUserInteraction();
            Assert.Equal(PetActivity.Sleeping, machine.State);
            Assert.False(machine.CanPlay);
            Assert.False(machine.RequestPlay());
            Assert.True(machine.RequestPet());
            machine.Update(TimeSpan.FromSeconds(3), Needs.Full, bowl);
            Assert.Equal(PetActivity.Sleeping, machine.State);
        }
        machine.Update(TimeSpan.FromSeconds(9), Needs.Full, bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
        machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
        Assert.Equal(PetActivity.Idle, machine.State);
        Assert.True(machine.CanPlay);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HungrySleeperSeeksExistingOrRefilledFoodThenStartsFullAwakePeriod(bool foodAlreadyPresent)
    {
        var machine = new PetBehaviorMachine(Tuning());
        var bowl = new Bowl(foodAlreadyPresent ? 25 : 0, 100);
        machine.Update(TimeSpan.FromSeconds(10), Needs.Full, bowl);
        machine.Update(TimeSpan.FromSeconds(15), Needs.FromValues(66, 100), bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
        if (!foodAlreadyPresent)
        {
            bowl.Fill(25);
        }

        machine.Update(TimeSpan.FromSeconds(1), Needs.FromValues(65, 100), bowl);
        Assert.Equal(PetActivity.SeekingFood, machine.State);
        machine.ArriveAtBowl(bowl);
        machine.Update(TimeSpan.FromSeconds(12), Needs.Full, bowl);
        Assert.Equal(PetActivity.Idle, machine.State);
        machine.Update(TimeSpan.FromSeconds(9), Needs.Full, bowl);
        Assert.NotEqual(PetActivity.Sleeping, machine.State);
        machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
        machine.Update(TimeSpan.FromSeconds(19), Needs.Full, bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
    }

    [Fact]
    public void EmptyBowlDoesNotWakeHungrySleeperAndFoodWinsAfterPetting()
    {
        var machine = new PetBehaviorMachine(Tuning());
        var bowl = new Bowl(0, 100);
        var hungry = Needs.FromValues(65, 100);
        machine.Update(TimeSpan.FromSeconds(10), Needs.Full, bowl);
        machine.Update(TimeSpan.FromSeconds(1), hungry, bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
        Assert.True(machine.RequestPet());
        bowl.Fill(25);
        machine.Update(TimeSpan.FromSeconds(3), hungry, bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
        machine.Update(TimeSpan.FromMilliseconds(200), hungry, bowl);
        Assert.Equal(PetActivity.SeekingFood, machine.State);
    }

    [Fact]
    public void CancellingPettingResumesSleepAndExitAlwaysWins()
    {
        var machine = new PetBehaviorMachine(Tuning());
        var bowl = new Bowl(0, 100);
        machine.Update(TimeSpan.FromSeconds(10), Needs.Full, bowl);
        machine.Update(TimeSpan.FromSeconds(19), Needs.Full, bowl);
        Assert.True(machine.RequestPet());
        machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
        machine.CancelInteraction();
        Assert.Equal(PetActivity.Sleeping, machine.State);
        machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
        Assert.Equal(PetActivity.Idle, machine.State);
        machine.Update(TimeSpan.FromSeconds(10), Needs.Full, bowl);
        Assert.True(machine.RequestPet());
        Assert.True(machine.RequestExit());
        machine.Update(TimeSpan.FromMinutes(1), Needs.Full, bowl);
        Assert.Equal(PetActivity.Exiting, machine.State);
    }

    [Fact]
    public void SleepUsesNormalDecayAndSleepingPettingAppliesItsCost()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var runtime = PetRuntime.CreateNew("Cat", Tuning(), now);
        runtime.Update(TimeSpan.FromSeconds(10), now);
        runtime.Update(TimeSpan.FromSeconds(5), now);
        Assert.Equal(PetActivity.Sleeping, runtime.Behavior.State);
        Assert.Equal(100 - 15.0 * 15 / 3600, runtime.Needs.Satiety, 6);
        Assert.Equal(100 - 12.0 * 15 / 3600, runtime.Needs.Happiness, 6);
        Assert.True(runtime.RequestPet());
        runtime.Update(TimeSpan.FromSeconds(3), now);
        Assert.Equal(95 - 15.0 * 18 / 3600, runtime.Needs.Satiety, 6);
        Assert.Equal(PetActivity.Sleeping, runtime.Behavior.State);
    }

    [Theory]
    [InlineData(true, 6, 15, 25)]
    [InlineData(false, 3, 5, 10)]
    public void InteractionEffectsAreProportionalAndCancellationStopsCharging(bool play, double seconds, double cost, double gain)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var tuning = new PetTuning { SatietyDecayPerMinute = 0, HappinessDecayPerMinute = 0 };
        var runtime = PetRuntime.Restore(new PetSnapshot { PetName = "Cat", Satiety = 100, Happiness = 20 }, tuning, now);
        Assert.True(play ? runtime.RequestPlay() : runtime.RequestPet());
        runtime.Update(TimeSpan.FromSeconds(seconds / 2), now);
        Assert.Equal(100 - cost / 2, runtime.Needs.Satiety, 6);
        Assert.Equal(20 + gain / 2, runtime.Needs.Happiness, 6);
        runtime.Behavior.CancelInteraction();
        runtime.Update(TimeSpan.FromSeconds(seconds), now);
        Assert.Equal(100 - cost / 2, runtime.Needs.Satiety, 6);
        Assert.Equal(20 + gain / 2, runtime.Needs.Happiness, 6);
    }
}
