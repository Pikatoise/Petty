#if DEBUG
using System.Reflection;
using Petty.Core.Behavior;
using Petty.Core.Diagnostics;
using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.Tests;

public class DebugTuningTests
{
    [Fact]
    public void BoundsNonFiniteValuesAndMaintainsThresholdOrder()
    {
        DebugTuning value = new DebugTuning { SatietyDecayPerMinute = double.NaN, HappinessDecayPerMinute = double.PositiveInfinity, Hungry = 30, Tired = 90, Recovery = 5, PlaySeconds = -1, PetSeconds = 500 }.Sanitized();
        Assert.Equal(15.0 / 60, value.SatietyDecayPerMinute);
        Assert.Equal(12.0 / 60, value.HappinessDecayPerMinute);
        Assert.Equal(30, value.Tired);
        Assert.Equal(30, value.Recovery);
        Assert.Equal(0.5, value.PlaySeconds);
        Assert.Equal(120, value.PetSeconds);
    }

    [Fact]
    public void UpdatedTuningChangesLiveSimulationWithoutReplacingPet()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var runtime = PetRuntime.CreateNew("Cat", new DebugTuning { SatietyDecayPerMinute = 0 }.ToPetTuning(), now);
        runtime.SetNeeds(80, 50);
        runtime.SetBowlFood(25);
        runtime.Update(TimeSpan.FromHours(1), now);
        Assert.Equal(80, runtime.Needs.Satiety, 4);
        PetSnapshot snapshot = runtime.Snapshot;
        runtime.SetTuning(new DebugTuning { SatietyDecayPerMinute = 10.0 / 60, Hungry = 75, PlaySeconds = 2 }.ToPetTuning());
        runtime.Update(TimeSpan.FromHours(1), now);
        Assert.Equal(70, runtime.Needs.Satiety, 4);
        Assert.Equal(PetActivity.SeekingFood, runtime.Behavior.State);
        Assert.Same(snapshot, runtime.Snapshot);
        Assert.Equal(25, runtime.ToSnapshot().BowlFood);
    }
    [Fact]
    public void DefaultsMatchProductionAndWanderBoundsStayOrdered()
    {
        var settings = new DebugTuning();
        var tuning = settings.ToPetTuning();
        foreach (PropertyInfo? property in typeof(PetTuning).GetProperties().Where(p => !p.GetMethod!.IsStatic))
        {
            Assert.Equal(property.GetValue(PetTuning.Default), property.GetValue(tuning));
        }

        Assert.Equal(1, tuning.SleepChance);
        Assert.Equal(1, settings.TimeScale);
        Assert.Equal(20, (settings with
        {
            TimeScale = 999
        }).Sanitized().TimeScale);
        Assert.Equal(1, (settings with
        {
            TimeScale = double.NaN
        }).Sanitized().TimeScale);
        DebugTuning adjusted = (settings with
        {
            WanderMinSeconds = 30,
            WanderMaxSeconds = 5,
            RunChancePercent = 150,
            SleepChancePercent = -1
        }).Sanitized();
        Assert.Equal(30, adjusted.WanderMaxSeconds);
        Assert.Equal(100, adjusted.RunChancePercent);
        Assert.Equal(0, adjusted.SleepChancePercent);
        DebugTuning range = (settings with
        {
            SleepMinSeconds = 700,
            SleepMaxSeconds = 200
        }).Sanitized();
        Assert.Equal(700, range.SleepMinSeconds);
        Assert.Equal(700, range.SleepMaxSeconds);
        DebugTuning awake = (settings with
        {
            AwakeMinSeconds = 700,
            AwakeMaxSeconds = 200
        }).Sanitized();
        Assert.Equal(700, awake.AwakeMaxSeconds);
    }

    [Fact]
    public void ChangingDurationKeepsCurrentInteractionConsistent()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var initial = new DebugTuning { SatietyDecayPerMinute = 0, HappinessDecayPerMinute = 0, PlaySeconds = 6 };
        var runtime = PetRuntime.CreateNew("Cat", initial.ToPetTuning(), now);
        runtime.SetNeeds(80, 20);
        Assert.True(runtime.RequestPlay());
        runtime.Update(TimeSpan.FromSeconds(2), now);
        runtime.SetTuning((initial with
        {
            PlaySeconds = 2
        }).ToPetTuning());
        runtime.Update(TimeSpan.FromSeconds(2), now);
        Assert.Equal(PetActivity.Playing, runtime.Behavior.State);
        runtime.Update(TimeSpan.FromSeconds(2), now);
        Assert.Equal(PetActivity.Idle, runtime.Behavior.State);
        Assert.Equal(45, runtime.Needs.Happiness, 5);
        Assert.True(runtime.RequestPlay());
        runtime.Update(TimeSpan.FromSeconds(2), now);
        Assert.Equal(PetActivity.Idle, runtime.Behavior.State);
    }

    [Fact]
    public void DebugActionsRespectInteractionAndEatingPriority()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var runtime = PetRuntime.CreateNew("Cat", new DebugTuning().ToPetTuning(), now);
        Assert.True(runtime.Behavior.RequestDebugActivity(PetActivity.Walking));
        Assert.True(runtime.Behavior.RequestDebugActivity(PetActivity.Sleeping));
        Assert.False(runtime.RequestPlay());
        Assert.True(runtime.Behavior.RequestDebugActivity(PetActivity.Idle));
        Assert.True(runtime.RequestPlay());
        Assert.False(runtime.Behavior.RequestDebugActivity(PetActivity.Running));
        runtime.Update(TimeSpan.FromSeconds(6), now);
        runtime.SetNeeds(1, 50);
        runtime.SetBowlFood(25);
        runtime.Update(TimeSpan.FromMilliseconds(200), now);
        runtime.NotifyArrived();
        Assert.Equal(PetActivity.Eating, runtime.Behavior.State);
        Assert.False(runtime.Behavior.RequestDebugActivity(PetActivity.Sleeping));
        runtime.RequestExit();
        Assert.False(runtime.Behavior.RequestDebugActivity(PetActivity.Idle));
    }

    [Fact]
    public void SleepRangeChangesApplyToNextSleep()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var settings = new DebugTuning { SleepMinSeconds = 2, SleepMaxSeconds = 2 };
        var runtime = PetRuntime.CreateNew("Cat", settings.ToPetTuning(), now);
        Assert.True(runtime.Behavior.RequestDebugActivity(PetActivity.Sleeping));
        runtime.Update(TimeSpan.FromSeconds(1), now);
        runtime.SetTuning((settings with
        {
            SleepMinSeconds = 10,
            SleepMaxSeconds = 10
        }).ToPetTuning());
        runtime.Update(TimeSpan.FromSeconds(1), now);
        Assert.Equal(PetActivity.Idle, runtime.Behavior.State);
        Assert.True(runtime.Behavior.RequestDebugActivity(PetActivity.Sleeping));
        runtime.Update(TimeSpan.FromSeconds(2), now);
        Assert.Equal(PetActivity.Sleeping, runtime.Behavior.State);
        runtime.Update(TimeSpan.FromSeconds(8), now);
        Assert.Equal(PetActivity.Idle, runtime.Behavior.State);
    }

}
#endif
