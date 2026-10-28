namespace Petty.Core.Domain;

/// <summary>
/// Every decay rate, threshold and timing used by the pet lives here instead of
/// being scattered through the code as magic numbers. The app may override the
/// defaults from a config file; tests construct instances with explicit values.
/// </summary>
public sealed class PetTuning
{
    // Needs decay.
    public double SatietyDecayPerHour { get; init; } = 4.0;
    public double HappinessDecayPerHour { get; init; } = 3.0;
    public TimeSpan MaxOfflineSpan { get; init; } = TimeSpan.FromDays(7);

    // Thresholds (0-100).
    public double HungryThreshold { get; init; } = 30.0;
    public double TiredThreshold { get; init; } = 20.0;
    public double SatietyRecoveryThreshold { get; init; } = 45.0;
    public double SleepHappinessThreshold { get; init; } = 45.0;

    // Bowl.
    public double BowlCapacity { get; init; } = 100.0;
    public double BowlFoodPerClick { get; init; } = 25.0;

    // Eating.
    public double EatFoodPerBite { get; init; } = 5.0;
    public double EatSatietyPerBite { get; init; } = 9.0;
    public TimeSpan EatBiteInterval { get; init; } = TimeSpan.FromSeconds(1.2);
    public TimeSpan MaxEatDuration { get; init; } = TimeSpan.FromSeconds(20);

    // Playing.
    public TimeSpan PlayDuration { get; init; } = TimeSpan.FromSeconds(12);
    public double PlayHappinessGain { get; init; } = 30.0;
    public double PlaySatietyCost { get; init; } = 2.0;

    // Behaviour pacing.
    public TimeSpan EnterDuration { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan ExitDuration { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan SleepAfterIdle { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan SleepDuration { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Safety caps: a walk normally ends when the pet reaches its target (the view
    /// reports the arrival), these only stop a walk that never arrives.
    /// </summary>
    public TimeSpan WalkDuration { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan RunDuration { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan WanderMinInterval { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan WanderMaxInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan SeekTimeout { get; init; } = TimeSpan.FromSeconds(120);
    public double RunChance { get; init; } = 0.2;

    // Runtime.
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan AutoSaveInterval { get; init; } = TimeSpan.FromMinutes(2);

    public static PetTuning Default { get; } = new();

    /// <summary>Applies values that cannot make the app fail, keeping the rest untouched.</summary>
    public PetTuning Sanitized()
    {
        static double Clamp(double value, double min, double max) =>
            double.IsNaN(value) ? min : Math.Clamp(value, min, max);

        return new PetTuning
        {
            SatietyDecayPerHour = Math.Max(0, SatietyDecayPerHour),
            HappinessDecayPerHour = Math.Max(0, HappinessDecayPerHour),
            MaxOfflineSpan = Positive(MaxOfflineSpan, TimeSpan.FromDays(7)),
            HungryThreshold = Clamp(HungryThreshold, 1, 100),
            TiredThreshold = Clamp(TiredThreshold, 0, 100),
            SatietyRecoveryThreshold = Clamp(SatietyRecoveryThreshold, 1, 100),
            SleepHappinessThreshold = Clamp(SleepHappinessThreshold, 0, 100),
            BowlCapacity = Math.Max(1, BowlCapacity),
            BowlFoodPerClick = Math.Max(1, BowlFoodPerClick),
            EatFoodPerBite = Math.Max(0.1, EatFoodPerBite),
            EatSatietyPerBite = Math.Max(0.1, EatSatietyPerBite),
            EatBiteInterval = Positive(EatBiteInterval, TimeSpan.FromSeconds(1)),
            MaxEatDuration = Positive(MaxEatDuration, TimeSpan.FromSeconds(20)),
            PlayDuration = Positive(PlayDuration, TimeSpan.FromSeconds(10)),
            PlayHappinessGain = Math.Max(1, PlayHappinessGain),
            PlaySatietyCost = Math.Max(0, PlaySatietyCost),
            EnterDuration = Positive(EnterDuration, TimeSpan.FromSeconds(3)),
            ExitDuration = Positive(ExitDuration, TimeSpan.FromSeconds(3)),
            SleepAfterIdle = Positive(SleepAfterIdle, TimeSpan.FromMinutes(2)),
            SleepDuration = Positive(SleepDuration, TimeSpan.FromMinutes(10)),
            WalkDuration = Positive(WalkDuration, TimeSpan.FromSeconds(60)),
            RunDuration = Positive(RunDuration, TimeSpan.FromSeconds(60)),
            WanderMinInterval = Positive(WanderMinInterval, TimeSpan.FromSeconds(3)),
            WanderMaxInterval = Positive(WanderMaxInterval, TimeSpan.FromSeconds(10)),
            SeekTimeout = Positive(SeekTimeout, TimeSpan.FromSeconds(120)),
            RunChance = Clamp(RunChance, 0, 1),
            TickInterval = Positive(TickInterval, TimeSpan.FromMilliseconds(200)),
            AutoSaveInterval = Positive(AutoSaveInterval, TimeSpan.FromMinutes(2)),
        };
    }

    private static TimeSpan Positive(TimeSpan value, TimeSpan fallback) => value > TimeSpan.Zero ? value : fallback;
}
