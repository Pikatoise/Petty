#if DEBUG
using Petty.Core.Domain;

namespace Petty.Core.Diagnostics;

/// <summary>Bounded debug-only overrides; production defaults remain the source of other tuning values.</summary>
public sealed record DebugTuning
{
    public double SatietyDecayPerMinute { get; init; } = 15.0 / 60;
    public double HappinessDecayPerMinute { get; init; } = 12.0 / 60;
    public double Hungry { get; init; } = 65;
    public double Tired { get; init; } = 35;
    public double Recovery { get; init; } = 75;
    public double PlaySeconds { get; init; } = 6;
    public double PetSeconds { get; init; } = 3;
    public double EatBiteSeconds { get; init; } = 1.2;
    public double AwakeMinSeconds { get; init; } = 360;
    public double AwakeMaxSeconds { get; init; } = 600;
    public double SleepMinSeconds { get; init; } = 360;
    public double SleepMaxSeconds { get; init; } = 600;
    public double WanderMinSeconds { get; init; } = 1;
    public double WanderMaxSeconds { get; init; } = 3;
    public double RunChancePercent { get; init; } = 65;
    public double SleepChancePercent { get; init; } = 100;
    public double TimeScale { get; init; } = 1;

    public DebugTuning Sanitized()
    {
        static double Bound(double value, double min, double max, double fallback) =>
            double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        double hungry = Bound(Hungry, 1, 100, 65);
        double wanderMin = Bound(WanderMinSeconds, 0.5, 600, 1);
        double awakeMin = Bound(AwakeMinSeconds, 1, 3600, 360);
        double sleepMin = Bound(SleepMinSeconds, 1, 3600, 360);
        return this with
        {
            SatietyDecayPerMinute = Bound(SatietyDecayPerMinute, 0, 10000.0 / 60, 15.0 / 60),
            HappinessDecayPerMinute = Bound(HappinessDecayPerMinute, 0, 10000.0 / 60, 12.0 / 60),
            Hungry = hungry,
            Tired = Math.Min(hungry, Bound(Tired, 0, 100, 35)),
            Recovery = Math.Max(hungry, Bound(Recovery, 1, 100, 75)),
            PlaySeconds = Bound(PlaySeconds, 0.5, 120, 6),
            PetSeconds = Bound(PetSeconds, 0.5, 120, 3),
            EatBiteSeconds = Bound(EatBiteSeconds, 0.2, 60, 1.2),
            AwakeMinSeconds = awakeMin,
            AwakeMaxSeconds = Math.Max(awakeMin, Bound(AwakeMaxSeconds, 1, 3600, 600)),
            SleepMinSeconds = sleepMin,
            SleepMaxSeconds = Math.Max(sleepMin, Bound(SleepMaxSeconds, 1, 3600, 600)),
            WanderMinSeconds = wanderMin,
            WanderMaxSeconds = Math.Max(wanderMin, Bound(WanderMaxSeconds, 0.5, 600, 3)),
            RunChancePercent = Bound(RunChancePercent, 0, 100, 65),
            SleepChancePercent = Bound(SleepChancePercent, 0, 100, 100),
            TimeScale = Bound(TimeScale, 0.1, 20, 1),
        };
    }

    public PetTuning ToPetTuning()
    {
        DebugTuning value = Sanitized();
        return new PetTuning
        {
            SatietyDecayPerMinute = value.SatietyDecayPerMinute,
            HappinessDecayPerMinute = value.HappinessDecayPerMinute,
            HungryThreshold = value.Hungry,
            TiredThreshold = value.Tired,
            SatietyRecoveryThreshold = value.Recovery,
            PlayDuration = TimeSpan.FromSeconds(value.PlaySeconds),
            PetDuration = TimeSpan.FromSeconds(value.PetSeconds),
            EatBiteInterval = TimeSpan.FromSeconds(value.EatBiteSeconds),
            AwakeMinDuration = TimeSpan.FromSeconds(value.AwakeMinSeconds),
            AwakeMaxDuration = TimeSpan.FromSeconds(value.AwakeMaxSeconds),
            SleepMinDuration = TimeSpan.FromSeconds(value.SleepMinSeconds),
            SleepMaxDuration = TimeSpan.FromSeconds(value.SleepMaxSeconds),
            WanderMinInterval = TimeSpan.FromSeconds(value.WanderMinSeconds),
            WanderMaxInterval = TimeSpan.FromSeconds(value.WanderMaxSeconds),
            RunChance = value.RunChancePercent / 100,
            SleepChance = value.SleepChancePercent / 100,
        };
    }
}
#endif
