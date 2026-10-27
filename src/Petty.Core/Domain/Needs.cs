namespace Petty.Core.Domain;

/// <summary>Normalised pet needs. Both values always stay inside 0..100.</summary>
public readonly record struct Needs(double Satiety, double Happiness)
{
    public const double Minimum = 0d;
    public const double Maximum = 100d;

    public static Needs Full => new(Maximum, Maximum);

    public static Needs FromValues(double satiety, double happiness) =>
        new(Clamp(satiety), Clamp(happiness));

    public Needs Advance(TimeSpan elapsed, PetTuning tuning)
    {
        if (elapsed <= TimeSpan.Zero) return this;
        var hours = elapsed.TotalHours;
        return FromValues(
            Satiety - (tuning.SatietyDecayPerHour * hours),
            Happiness - (tuning.HappinessDecayPerHour * hours));
    }

    public Needs WithSatiety(double value) => new(Clamp(value), Happiness);

    public Needs WithHappiness(double value) => new(Satiety, Clamp(value));

    public Needs AddSatiety(double delta) => WithSatiety(Satiety + delta);

    public Needs AddHappiness(double delta) => WithHappiness(Happiness + delta);

    public bool IsHungry(PetTuning tuning) => Satiety <= tuning.HungryThreshold;

    public bool IsStarving(PetTuning tuning) => Satiety <= tuning.TiredThreshold;

    public bool IsTired(PetTuning tuning) => Satiety <= tuning.TiredThreshold;

    public static double Clamp(double value) =>
        double.IsNaN(value) ? Minimum : Math.Clamp(value, Minimum, Maximum);
}
