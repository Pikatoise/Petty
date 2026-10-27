namespace Petty.Core.Domain;

/// <summary>
/// Food inventory of the bowl. Independent from the cat's satiety and never
/// allowed to go negative or above capacity.
/// </summary>
public sealed class Bowl
{
    private const double EmptyEpsilon = 0.001;

    public Bowl(double food, double capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        Capacity = capacity;

        Food = ClampFood(food);
    }

    public double Capacity { get; }

    public double Food { get; private set; }

    public bool HasFood => Food > EmptyEpsilon;

    /// <summary>
    /// Adds food and returns how much actually fitted into the bowl.
    /// </summary>
    public double Fill(double amount)
    {
        if (amount <= 0 || double.IsNaN(amount))
            return 0;

        var addedAmount = Math.Min(amount, Capacity - Food);

        Food += addedAmount;

        return addedAmount;
    }

    /// <summary>
    /// Removes food and returns how much was actually consumed.
    /// </summary>
    public double Consume(double amount)
    {
        if (amount <= 0 || double.IsNaN(amount))
            return 0;
        var consumed = Math.Min(amount, Food);

        Food -= consumed;

        if (Food <= EmptyEpsilon)
            Food = 0;

        return consumed;
    }

    private double ClampFood(double value) =>
        double.IsNaN(value) ? 0 : Math.Clamp(value, 0, Capacity);
}
