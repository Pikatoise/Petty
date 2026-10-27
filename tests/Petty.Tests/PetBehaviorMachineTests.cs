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
        SleepDuration = TimeSpan.FromMinutes(10),
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
