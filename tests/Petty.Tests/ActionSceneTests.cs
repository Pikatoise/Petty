using Petty.Core.Behavior;
using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.Tests;

public class ActionSceneTests
{
    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.7)]
    public void BallTrajectoryDoesNotSnapVelocityAtPhaseBoundaries(double boundary)
    {
        const double step = 0.00001;
        var at = ActionScene.Sample(PetActivity.Playing, boundary).PropOffset;
        var before = (at - ActionScene.Sample(PetActivity.Playing, boundary - step).PropOffset) / step;
        var after = (ActionScene.Sample(PetActivity.Playing, boundary + step).PropOffset - at) / step;
        Assert.True(Math.Abs(after - before) < 5, $"Ball velocity snapped from {before:F1} to {after:F1} at {boundary}.");
    }

    [Fact]
    public void BallContactsMatchExtendedPawFramesAndBreedContactHeight()
    {
        for (var hit = 0; hit <= 6; hit++)
        {
            var pose = ActionScene.SamplePlay(0.3 + hit * 0.6, 6, 12, 10, contactLift: 12);
            Assert.Equal(hit % 2 == 0 ? 3 : 9, pose.FrameIndex);
            Assert.Equal(0, pose.PropOffset, 6);
            Assert.Equal(-12, pose.PropHeight, 6);
        }
    }

    [Fact]
    public void BallStaysCloseAndMovesSmoothlyBetweenPawContacts()
    {
        var previous = ActionScene.SamplePlay(0.3, 6, 12, 10, contactLift: 15);
        for (var tick = 1; tick < 180; tick++)
        {
            var pose = ActionScene.SamplePlay(0.3 + tick * 0.02, 6, 12, 10, contactLift: 15);
            Assert.InRange(pose.PropOffset, 0, 36.001);
            Assert.InRange(pose.PropHeight, -20, 0);
            Assert.InRange(Math.Abs(pose.PropOffset - previous.PropOffset), 0, 4);
            Assert.InRange(Math.Abs(pose.PropHeight - previous.PropHeight), 0, 4);
            previous = pose;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(20)]
    public void FinalRollLeavesDisplayAndStopsFurtherPawStrikes(double duration)
    {
        var end = ActionScene.SamplePlay(duration, duration, 12, 10, 1800, 12);
        Assert.Equal(1800, end.PropOffset, 5);
        Assert.Equal(0, end.PropHeight, 5);
        Assert.Equal(end, ActionScene.SamplePlay(duration + 5, duration, 12, 10, 1800, 12));
        var before = ActionScene.SamplePlay(duration * 0.98, duration, 12, 10, 1800, 12);
        Assert.Equal(end.FrameIndex, before.FrameIndex);
        Assert.True(end.PropOffset > before.PropOffset);
    }

    [Fact]
    public void InteractionsRejectOverlapAndApplyFullEffectOnce()
    {
        var tuning = new PetTuning { SatietyDecayPerMinute = 0, HappinessDecayPerMinute = 0 };
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
