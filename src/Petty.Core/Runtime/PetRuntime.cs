using Petty.Core.Behavior;
using Petty.Core.Domain;

namespace Petty.Core.Runtime;

/// <summary>
/// Owns the live pet state for one session. The UI layer feeds elapsed time in and
/// reads needs/state back; all domain rules stay here and are unit-testable.
/// </summary>
public sealed class PetRuntime
{
    private PetTuning _tuning;
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

#if DEBUG
    public void SetTuning(PetTuning tuning)
    {
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        Behavior.SetTuning(tuning);
    }

    public void SetNeeds(double satiety, double happiness) => Needs = Needs.FromValues(satiety, happiness);
    public void SetBowlFood(double amount)
    {
        Bowl.Consume(Bowl.Food);
        Bowl.Fill(amount);
    }
#endif

    public PetSnapshot Snapshot
    {
        get;
    }

    public Needs Needs
    {
        get; private set;
    }

    public Bowl Bowl
    {
        get;
    }

    public PetBehaviorMachine Behavior
    {
        get;
    }

    public DateTimeOffset LastUpdatedUtc
    {
        get; private set;
    }

    public bool IsExitAnimationComplete =>
        Behavior.State is PetActivity.Exiting && Behavior.StateElapsed >= _tuning.ExitDuration;

    public static PetRuntime Restore(
        PetSnapshot snapshot,
        PetTuning tuning,
        DateTimeOffset nowUtc,
        Random? random = null)
    {
        var runtime = new PetRuntime(snapshot.Normalized(tuning), tuning, random);
        runtime.LastUpdatedUtc = nowUtc;
        return runtime;
    }

    public static PetRuntime CreateNew(string petName, PetTuning tuning, DateTimeOffset nowUtc, Random? random = null)
    {
        if (!PetNameRules.TryNormalize(petName, out string? name, out string? error))
        {
            throw new ArgumentException(error, nameof(petName));
        }

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
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta > _tuning.MaxOfflineSpan)
        {
            delta = _tuning.MaxOfflineSpan;
        }

        Needs = Needs.Advance(delta, _tuning);
        PetActivity previous = Behavior.State;
        TimeSpan remaining = previous switch
        {
            PetActivity.Playing or PetActivity.Petting => Behavior.InteractionDuration - Behavior.StateElapsed,
            _ => TimeSpan.Zero,
        };
        double actionSeconds = Math.Max(0, Math.Min(delta.TotalSeconds, remaining.TotalSeconds));
        if (previous is PetActivity.Playing or PetActivity.Petting)
        {
            TimeSpan duration = Behavior.InteractionDuration;
            double happiness = previous is PetActivity.Playing ? _tuning.PlayHappinessGain : _tuning.PetHappinessGain;
            double satiety = previous is PetActivity.Playing ? _tuning.PlaySatietyCost : _tuning.PetSatietyCost;
            double fraction = actionSeconds / Math.Max(0.001, duration.TotalSeconds);
            Needs = Needs.AddHappiness(happiness * fraction).AddSatiety(-satiety * fraction);
        }
        Behavior.Update(delta, Needs, Bowl);

        switch (Behavior.State)
        {
            case PetActivity.Eating:
                _biteTimer += delta;
                while (_biteTimer >= _tuning.EatBiteInterval && Bowl.HasFood)
                {
                    _biteTimer -= _tuning.EatBiteInterval;
                    double consumed = Bowl.Consume(_tuning.EatFoodPerBite);
                    if (consumed <= 0)
                    {
                        break;
                    }

                    double share = Math.Min(1, consumed / _tuning.EatFoodPerBite);
                    Needs = Needs.AddSatiety(_tuning.EatSatietyPerBite * share);
                }

                if (!Bowl.HasFood)
                {
                    Behavior.BowlEmptied();
                }

                break;

            default:
                _biteTimer = TimeSpan.Zero;
                break;
        }

        LastUpdatedUtc = nowUtc;
    }

    public bool RequestPlay() => Behavior.RequestPlay();

    public bool RequestPet() => Behavior.RequestPet();

    public bool RequestExit() => Behavior.RequestExit();

    public void OnUserInteraction() => Behavior.OnUserInteraction();

    /// <summary>The view reports that the pet reached its walk target; food seekers also eat here.</summary>
    public void NotifyArrived()
    {
        if (Behavior.State is PetActivity.SeekingFood)
        {
            Behavior.ArriveAtBowl(Bowl);
        }
        else
        {
            Behavior.NotifyArrived();
        }
    }

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
