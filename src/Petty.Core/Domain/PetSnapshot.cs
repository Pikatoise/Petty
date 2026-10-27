using System.Text.Json.Serialization;

namespace Petty.Core.Domain;

/// <summary>Persisted pet state (schema v1). Everything needed to resume a pet lives here.</summary>
public sealed class PetSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string PetName { get; set; } = string.Empty;

    public string BreedId { get; set; } = PetCatalog.DefaultBreedId;

    public string CoatId { get; set; } = PetCatalog.DefaultCoatId;

    public string AssetId { get; set; } = PetCatalog.DefaultAssetId;

    public double Satiety { get; set; } = Needs.Maximum;

    public double Happiness { get; set; } = Needs.Maximum;

    public double BowlFood { get; set; }

    public double BowlRelativeX { get; set; } = 0.65;

    public double BowlBottomOffset { get; set; } = 120;

    public string? DisplayDeviceName { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset LastUpdatedUtc { get; set; }

    [JsonIgnore]
    public Needs Needs => Domain.Needs.FromValues(Satiety, Happiness);

    [JsonIgnore]
    public BowlPlacementState Placement => new(BowlRelativeX, BowlBottomOffset, DisplayDeviceName);

    /// <summary>Repairs values that would otherwise break rendering or save the pet in an invalid state.</summary>
    public PetSnapshot Normalized(PetTuning tuning)
    {
        PetNameRules.TryNormalize(PetName, out var name, out _);

        return new PetSnapshot
        {
            SchemaVersion = CurrentSchemaVersion,
            PetName = string.IsNullOrWhiteSpace(name) ?
                "Kitty" : name,

            BreedId = string.IsNullOrWhiteSpace(BreedId) ?
                PetCatalog.DefaultBreedId : BreedId,

            CoatId = string.IsNullOrWhiteSpace(CoatId) ?
                PetCatalog.DefaultCoatId : CoatId,

            AssetId = string.IsNullOrWhiteSpace(AssetId) ?
                PetCatalog.DefaultAssetId : AssetId,

            Satiety = NeedClamp(Satiety),
            Happiness = NeedClamp(Happiness),
            BowlFood = double.IsNaN(BowlFood) ?
                0 : Math.Clamp(BowlFood, 0, tuning.BowlCapacity),

            BowlRelativeX = double.IsNaN(BowlRelativeX) ?
                0.65 : Math.Clamp(BowlRelativeX, BowlPlacement.MinRelativeX, BowlPlacement.MaxRelativeX),

            BowlBottomOffset = double.IsNaN(BowlBottomOffset) ?
                120 : Math.Max(0, BowlBottomOffset),

            DisplayDeviceName = string.IsNullOrWhiteSpace(DisplayDeviceName) ?
                null : DisplayDeviceName,

            CreatedUtc = CreatedUtc,
            LastUpdatedUtc = LastUpdatedUtc,
        };
    }

    public PetSnapshot WithBowlPlacement(BowlPlacementState placement)
    {
        BowlRelativeX = placement.RelativeX;
        BowlBottomOffset = placement.BottomOffset;
        DisplayDeviceName = placement.DisplayDeviceName;
        return this;
    }

    private static double NeedClamp(double value) =>
        double.IsNaN(value) ? 0 : Math.Clamp(value, Needs.Minimum, Needs.Maximum);
}
