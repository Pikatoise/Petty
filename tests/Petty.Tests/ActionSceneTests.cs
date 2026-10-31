using Petty.Core.Behavior;
using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.Tests;

public class ActionSceneTests
{
    [Fact]
    public void InteractionsRejectOverlapAndApplyFullEffectOnce()
    {
        var tuning = new PetTuning { SatietyDecayPerHour = 0, HappinessDecayPerHour = 0 };
        var now = DateTimeOffset.UtcNow;
        var runtime = PetRuntime.Restore(new PetSnapshot { PetName = "Cat", Satiety = 80, Happiness = 20 }, tuning, now);
        Assert.True(runtime.RequestPet());
        Assert.False(runtime.RequestPet());
        Assert.False(runtime.RequestPlay());
        runtime.Update(TimeSpan.FromSeconds(4), now);
        Assert.Equal(PetActivity.Idle, runtime.Behavior.State);
        Assert.Equal(32, runtime.Needs.Happiness, 5);
        runtime.Update(TimeSpan.FromSeconds(1), now);
        Assert.Equal(32, runtime.Needs.Happiness, 5);
        Assert.True(runtime.RequestPlay());
        runtime.Update(TimeSpan.FromSeconds(7), now);
        Assert.Equal(62, runtime.Needs.Happiness, 5);
        Assert.Equal(78, runtime.Needs.Satiety, 5);
    }

    [Fact]
    public void SceneTrajectoriesFinishAwayAndReturnHand()
    {
        Assert.True(ActionScene.Sample(PetActivity.Playing, 1).PropOffset > 600);
        Assert.Equal(-35, ActionScene.Sample(PetActivity.Petting, 1).PropHeight);
    }

    [Fact]
    public void EatingAndExitRejectInteractions()
    {
        var runtime = PetRuntime.Restore(new PetSnapshot { PetName = "Cat", Satiety = 1, Happiness = 50, BowlFood = 25 }, new PetTuning(), DateTimeOffset.UtcNow);
        runtime.Update(TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow);
        runtime.NotifyArrived();
        Assert.Equal(PetActivity.Eating, runtime.Behavior.State);
        Assert.False(runtime.RequestPlay());
        Assert.False(runtime.RequestPet());
        runtime.RequestExit();
        Assert.False(runtime.RequestPet());
    }
}
