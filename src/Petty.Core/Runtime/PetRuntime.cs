using Petty.Core.Behavior;
using Petty.Core.Domain;

namespace Petty.Core.Runtime;

/// <summary>Bridge between persisted state, needs, bowl and the behaviour machine.</summary>
public static class OfflineProgression
{
    public readonly record struct Result(Needs Needs, TimeSpan AppliedElapsed, bool ClockWentBackwards);

    public static Result Apply(Needs needs, DateTimeOffset lastUpdatedUtc, DateTimeOffset nowUtc, PetTuning tuning)
    {
        if (lastUpdatedUtc == default)
            return new Result(needs, TimeSpan.Zero, false);

        var elapsed = nowUtc - lastUpdatedUtc;
        if (elapsed < TimeSpan.Zero)
            return new Result(needs, TimeSpan.Zero, true);

        if (elapsed > tuning.MaxOfflineSpan)
            elapsed = tuning.MaxOfflineSpan;

        if (elapsed <= TimeSpan.Zero)
            return new Result(needs, TimeSpan.Zero, false);

        return new Result(needs.Advance(elapsed, tuning), elapsed, false);
    }
}

/// <summary>
/// Owns the live pet state for one session. The UI layer feeds elapsed time in and
/// reads needs/state back; all domain rules stay here and are unit-testable.
/// </summary>
public sealed class PetRuntime
{
    private readonly PetTuning _tuning;
    private TimeSpan _biteTimer;

    private PetRuntime(PetSnapshot snapshot, PetTuning tuning, Random? random)
    {
        _tuning = tuning;
        Snapshot = snapshot;
        Needs = snapshot.Needs;
        Bowl = new Bowl(snapshot.BowlFood, tuning.BowlCapacity);
        Behavior = new PetBehaviorMachine(tuning, random);
        LastUpdatedUtc = snapshot.LastUpdatedUtc;
    }

    public PetSnapshot Snapshot { get; }

    public Needs Needs { get; private set; }

    public Bowl Bowl { get; }

    public PetBehaviorMachine Behavior { get; }

    public DateTimeOffset LastUpdatedUtc { get; private set; }

    public bool IsExitAnimationComplete =>
        Behavior.State is PetActivity.Exiting && Behavior.StateElapsed >= _tuning.ExitDuration;

    public static PetRuntime Restore(
        PetSnapshot snapshot,
        PetTuning tuning,
        DateTimeOffset nowUtc,
        Random? random = null)
    {
        var runtime = new PetRuntime(snapshot.Normalized(tuning), tuning, random);
        var offline = OfflineProgression.Apply(runtime.Needs, snapshot.LastUpdatedUtc, nowUtc, tuning);
        runtime.Needs = offline.Needs;
        runtime.LastUpdatedUtc = nowUtc;
        return runtime;
    }

    public static PetRuntime CreateNew(string petName, PetTuning tuning, DateTimeOffset nowUtc, Random? random = null)
    {
        if (!PetNameRules.TryNormalize(petName, out var name, out var error))
            throw new ArgumentException(error, nameof(petName));

        var snapshot = new PetSnapshot
        {
            PetName = name,
            BreedId = PetCatalog.DefaultBreedId,
            CoatId = PetCatalog.DefaultCoatId,
            AssetId = PetCatalog.DefaultAssetId,
            Satiety = Needs.Maximum,
            Happiness = Needs.Maximum,
            BowlFood = 0,
            CreatedUtc = nowUtc,
            LastUpdatedUtc = nowUtc,
        };

        return new PetRuntime(snapshot, tuning, random);
    }

    public void Update(TimeSpan delta, DateTimeOffset nowUtc)
    {
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        if (delta > TimeSpan.FromMinutes(5)) delta = TimeSpan.FromMinutes(5);

        Needs = Needs.Advance(delta, _tuning);
        Behavior.Update(delta, Needs, Bowl);

        switch (Behavior.State)
        {
            case PetActivity.Playing:
                var total = Math.Max(1, _tuning.PlayDuration.TotalSeconds);
                var fraction = Math.Min(1, delta.TotalSeconds / total);
                Needs = Needs
                    .AddHappiness(_tuning.PlayHappinessGain * fraction)
                    .AddSatiety(-_tuning.PlaySatietyCost * fraction);
                _biteTimer = TimeSpan.Zero;
                break;

            case PetActivity.Eating:
                _biteTimer += delta;
                while (_biteTimer >= _tuning.EatBiteInterval && Bowl.HasFood)
                {
                    _biteTimer -= _tuning.EatBiteInterval;
                    var consumed = Bowl.Consume(_tuning.EatFoodPerBite);
                    if (consumed <= 0) break;
                    var share = Math.Min(1, consumed / _tuning.EatFoodPerBite);
                    Needs = Needs.AddSatiety(_tuning.EatSatietyPerBite * share);
                }

                if (!Bowl.HasFood) Behavior.BowlEmptied();
                break;

            default:
                _biteTimer = TimeSpan.Zero;
                break;
        }

        LastUpdatedUtc = nowUtc;
    }

    public bool RequestPlay() => Behavior.RequestPlay();

    public bool RequestExit() => Behavior.RequestExit();

    public void OnUserInteraction() => Behavior.OnUserInteraction();

    public void NotifyArrivedAtBowl() => Behavior.ArriveAtBowl(Bowl);

    /// <summary>Adds one portion to the bowl; returns the amount that actually fitted.</summary>
    public double RefillBowl(double? amount = null) => Bowl.Fill(amount ?? _tuning.BowlFoodPerClick);

    public void UpdateBowlPlacement(BowlPlacementState placement) => Snapshot.WithBowlPlacement(placement);

    public PetSnapshot ToSnapshot()
    {
        Snapshot.Satiety = Needs.Satiety;
        Snapshot.Happiness = Needs.Happiness;
        Snapshot.BowlFood = Bowl.Food;
        Snapshot.LastUpdatedUtc = LastUpdatedUtc;
        Snapshot.SchemaVersion = PetSnapshot.CurrentSchemaVersion;
        return Snapshot;
    }
}
