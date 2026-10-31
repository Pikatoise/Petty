using Petty.Core.Behavior;
using Petty.Core.Domain;

namespace Petty.Tests;

public class PetBehaviorMachineTests
{
    private static PetTuning Tuning() => new()
    {
        EnterDuration = TimeSpan.FromSeconds(3),
        ExitDuration = TimeSpan.FromSeconds(3),
        PlayDuration = TimeSpan.FromSeconds(10),
        SleepAfterIdle = TimeSpan.FromSeconds(60),
        SleepMinDuration = TimeSpan.FromMinutes(10),
        SleepMaxDuration = TimeSpan.FromMinutes(10),
        WalkDuration = TimeSpan.FromSeconds(2),
        RunDuration = TimeSpan.FromSeconds(1),
        WanderMinInterval = TimeSpan.FromHours(1),
        WanderMaxInterval = TimeSpan.FromHours(1),
        MaxEatDuration = TimeSpan.FromSeconds(60),
    };

    private static Needs HungryNotStarving => Needs.FromValues(25, 60);

    [Fact]
    public void EnteringBecomesIdleAfterEnterDuration()
    {
        var machine = new PetBehaviorMachine(Tuning(), new Random(1));
        machine.StartEntering();

        machine.Update(TimeSpan.FromSeconds(2), Needs.Full, new Bowl(0, 100));
        Assert.Equal(PetActivity.Entering, machine.State);

        machine.Update(TimeSpan.FromSeconds(1.5), Needs.Full, new Bowl(0, 100));
        Assert.Equal(PetActivity.Idle, machine.State);
    }

    [Fact]
    public void ExitHasPriorityOverPlayingAndBlocksNewPlay()
    {
        var machine = new PetBehaviorMachine(Tuning(), new Random(1));

        Assert.True(machine.RequestPlay());
        Assert.Equal(PetActivity.Playing, machine.State);

        Assert.True(machine.RequestExit());
        Assert.Equal(PetActivity.Exiting, machine.State);

        Assert.False(machine.RequestPlay());
        Assert.False(machine.RequestExit());
    }

    [Fact]
    public void PlayingEndsAfterPlayDuration()
    {
        var machine = new PetBehaviorMachine(Tuning(), new Random(1));
        machine.RequestPlay();

        machine.Update(TimeSpan.FromSeconds(11), Needs.Full, new Bowl(0, 100));

        Assert.Equal(PetActivity.Idle, machine.State);
    }

    [Fact]
    public void HungryPetWithFoodSeeksTheBowl()
    {
        var machine = new PetBehaviorMachine(Tuning(), new Random(1));
        var bowl = new Bowl(50, 100);

        machine.Update(TimeSpan.FromMilliseconds(200), HungryNotStarving, bowl);

        Assert.Equal(PetActivity.SeekingFood, machine.State);
    }

    [Fact]
    public void HungryPetWithoutFoodLooksTired()
    {
        var machine = new PetBehaviorMachine(Tuning(), new Random(1));

        machine.Update(TimeSpan.FromMilliseconds(200), Needs.FromValues(15, 60), new Bowl(0, 100));

        Assert.Equal(PetActivity.Tired, machine.State);
    }

    [Fact]
    public void ArrivingAtBowlStartsWithFoodAndStopsWithEmptyBowl()
    {
        var machine = new PetBehaviorMachine(Tuning(), new Random(1));
        var bowl = new Bowl(50, 100);

        machine.Update(TimeSpan.FromMilliseconds(200), HungryNotStarving, bowl);
        machine.ArriveAtBowl(bowl);
        Assert.Equal(PetActivity.Eating, machine.State);

        bowl.Consume(bowl.Food);
        machine.Update(TimeSpan.FromMilliseconds(200), HungryNotStarving, bowl);
        Assert.Equal(PetActivity.Idle, machine.State);
    }

    [Fact]
    public void ArrivingAtEmptyBowlEndsInTiredState()
    {
        var machine = new PetBehaviorMachine(Tuning(), new Random(1));
        var bowl = new Bowl(50, 100);

        machine.Update(TimeSpan.FromMilliseconds(200), HungryNotStarving, bowl);
        bowl.Consume(bowl.Food);
        machine.ArriveAtBowl(bowl);

        Assert.Equal(PetActivity.Tired, machine.State);
    }

    [Fact]
    public void SleepsAfterIdlePeriodAndWakesUpOnInteraction()
    {
        var machine = new PetBehaviorMachine(Tuning(), new Random(1));

        machine.Update(TimeSpan.FromSeconds(61), Needs.FromValues(80, 80), new Bowl(0, 100));
        Assert.Equal(PetActivity.Sleeping, machine.State);

        machine.OnUserInteraction();
        Assert.Equal(PetActivity.Idle, machine.State);
    }

    [Fact]
    public void CertainSleepWorksWhenHappinessIsLow()
    {
        var tuning = new PetTuning
        {
            SleepAfterIdle = TimeSpan.FromSeconds(2),
            SleepChance = 1,
            WanderMinInterval = TimeSpan.FromHours(1),
            WanderMaxInterval = TimeSpan.FromHours(1)
        };
        var machine = new PetBehaviorMachine(tuning, new Random(1));
        machine.Update(TimeSpan.FromSeconds(2), Needs.FromValues(80, 20), new Bowl(0, 100));
        Assert.Equal(PetActivity.Sleeping, machine.State);
    }

    [Fact]
    public void StarvingPetWithoutFoodCanSleepButSeeksFoodFirstWhenAvailable()
    {
        var tuning = new PetTuning
        {
            SleepAfterIdle = TimeSpan.FromSeconds(2),
            SleepChance = 1,
            WanderMinInterval = TimeSpan.FromHours(1),
            WanderMaxInterval = TimeSpan.FromHours(1)
        };
        var machine = new PetBehaviorMachine(tuning, new Random(1));
        var needs = Needs.FromValues(0, 0);
        machine.Update(TimeSpan.FromMilliseconds(200), needs, new Bowl(0, 100));
        Assert.Equal(PetActivity.Tired, machine.State);
        machine.Update(TimeSpan.FromSeconds(2), needs, new Bowl(0, 100));
        Assert.Equal(PetActivity.Sleeping, machine.State);
        machine.Update(TimeSpan.FromMilliseconds(200), needs, new Bowl(25, 100));
        Assert.Equal(PetActivity.SeekingFood, machine.State);
    }

    [Fact]
    public void SleepProbabilityIsCheckedOncePerIdleInterval()
    {
        var tuning = new PetTuning
        {
            SleepAfterIdle = TimeSpan.FromSeconds(2),
            SleepChance = 0.5,
            WanderMinInterval = TimeSpan.FromHours(1),
            WanderMaxInterval = TimeSpan.FromHours(1)
        };
        var machine = new PetBehaviorMachine(tuning, new SequenceRandom(0.5, 0.8, 0.2));
        var bowl = new Bowl(0, 100);
        machine.Update(TimeSpan.FromSeconds(2), Needs.Full, bowl);
        Assert.Equal(PetActivity.Idle, machine.State);
        machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
        Assert.Equal(PetActivity.Idle, machine.State);
        machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
    }

    [Fact]
    public void IdleTimeAccumulatesAcrossWalksAndZeroChanceNeverSleeps()
    {
        var tuning = new PetTuning
        {
            SleepAfterIdle = TimeSpan.FromSeconds(5),
            SleepChance = 1,
            WanderMinInterval = TimeSpan.FromSeconds(2),
            WanderMaxInterval = TimeSpan.FromSeconds(2),
            RunChance = 0
        };
        var machine = new PetBehaviorMachine(tuning, new Random(1));
        var bowl = new Bowl(0, 100);
        machine.Update(TimeSpan.FromSeconds(2), Needs.Full, bowl);
        Assert.Equal(PetActivity.Walking, machine.State);
        machine.NotifyArrived();
        machine.Update(TimeSpan.FromSeconds(2), Needs.Full, bowl);
        machine.NotifyArrived();
        machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
        var noSleep = new PetBehaviorMachine(new PetTuning
        {
            SleepAfterIdle = TimeSpan.FromSeconds(1),
            SleepChance = 0,
            WanderMinInterval = TimeSpan.FromHours(1),
            WanderMaxInterval = TimeSpan.FromHours(1)
        }, new Random(1));
        noSleep.Update(TimeSpan.FromSeconds(10), Needs.Full, bowl);
        Assert.Equal(PetActivity.Idle, noSleep.State);
    }

    private sealed class SequenceRandom(params double[] values): Random
    {
        private int _index;
        public override double NextDouble() => values[Math.Min(_index++, values.Length - 1)];
    }

    [Fact]
    public void SleepDurationIsSampledOncePerSleepWithinConfiguredRange()
    {
        var tuning = new PetTuning
        {
            SleepAfterIdle = TimeSpan.FromSeconds(1),
            SleepChance = 1,
            SleepMinDuration = TimeSpan.FromSeconds(100),
            SleepMaxDuration = TimeSpan.FromSeconds(200),
            WanderMinInterval = TimeSpan.FromHours(1),
            WanderMaxInterval = TimeSpan.FromHours(1)
        };
        var machine = new PetBehaviorMachine(tuning, new SequenceRandom(0, 0.25, 0.5, 0.75));
        var bowl = new Bowl(0, 100);
        machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
        machine.Update(TimeSpan.FromSeconds(124.9), Needs.Full, bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
        machine.Update(TimeSpan.FromSeconds(0.1), Needs.Full, bowl);
        Assert.Equal(PetActivity.Idle, machine.State);
        machine.Update(TimeSpan.FromSeconds(1), Needs.Full, bowl);
        machine.Update(TimeSpan.FromSeconds(174.9), Needs.Full, bowl);
        Assert.Equal(PetActivity.Sleeping, machine.State);
        machine.Update(TimeSpan.FromSeconds(0.1), Needs.Full, bowl);
        Assert.Equal(PetActivity.Idle, machine.State);
    }

    [Fact]
    public void HungerWakesSleepingPetWhenFoodIsAvailable()
    {
        var machine = new PetBehaviorMachine(Tuning(), new Random(1));

        machine.Update(TimeSpan.FromSeconds(61), Needs.FromValues(80, 80), new Bowl(0, 100));
        Assert.Equal(PetActivity.Sleeping, machine.State);

        machine.Update(TimeSpan.FromMilliseconds(200), HungryNotStarving, new Bowl(40, 100));
        Assert.Equal(PetActivity.SeekingFood, machine.State);
    }

    [Fact]
    public void TransitionsAreReportedWithReasons()
    {
        var machine = new PetBehaviorMachine(Tuning(), new Random(1));
        var transitions = new List<BehaviorTransition>();
        machine.Transitioned += (_, e) => transitions.Add(e);

        machine.Update(TimeSpan.FromMilliseconds(200), HungryNotStarving, new Bowl(40, 100));

        var transition = Assert.Single(transitions);
        Assert.Equal(PetActivity.Idle, transition.From);
        Assert.Equal(PetActivity.SeekingFood, transition.To);
        Assert.Equal("hungry", transition.Reason);
    }

    [Fact]
    public void IdlePetStartsWanderingWhenTheCountdownExpires()
    {
        var tuning = new PetTuning
        {
            WanderMinInterval = TimeSpan.FromMilliseconds(100),
            WanderMaxInterval = TimeSpan.FromMilliseconds(100),
            RunChance = 0,
            WalkDuration = TimeSpan.FromSeconds(2),
        };
        var machine = new PetBehaviorMachine(tuning, new Random(7));

        machine.Update(TimeSpan.FromMilliseconds(200), Needs.Full, new Bowl(0, 100));

        Assert.Equal(PetActivity.Walking, machine.State);
    }

    [Fact]
    public void ArrivalEndsAWalkBeforeTheSafetyTimer()
    {
        var tuning = new PetTuning
        {
            WanderMinInterval = TimeSpan.FromMilliseconds(100),
            WanderMaxInterval = TimeSpan.FromMilliseconds(100),
            RunChance = 0,
            WalkDuration = TimeSpan.FromSeconds(60),
        };
        var machine = new PetBehaviorMachine(tuning, new Random(7));
        machine.Update(TimeSpan.FromMilliseconds(200), Needs.Full, new Bowl(0, 100));
        Assert.Equal(PetActivity.Walking, machine.State);

        machine.NotifyArrived();

        Assert.Equal(PetActivity.Idle, machine.State);
    }

    [Fact]
    public void ArrivalDoesNotOverrideExitOrPlay()
    {
        var exiting = new PetBehaviorMachine(Tuning(), new Random(1));
        exiting.RequestExit();
        exiting.NotifyArrived();
        Assert.Equal(PetActivity.Exiting, exiting.State);

        var playing = new PetBehaviorMachine(Tuning(), new Random(1));
        playing.RequestPlay();
        playing.NotifyArrived();
        Assert.Equal(PetActivity.Playing, playing.State);
    }
}
