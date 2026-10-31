namespace Petty.Core.Domain;

/// <summary>
/// New-pet defaults and the explicit appearance migration from the retired prototype.
/// Installed breed choices come from sprite metadata, not this class.
/// </summary>
public static class PetCatalog
{
    public const string DefaultBreedId = "british-shorthair";
    public const string DefaultCoatId = "blue";
    public const string DefaultAssetId = "british-shorthair-blue";
    public const string LegacyAssetId = "cat-orange-white";

    /// <summary>Call only after the replacement atlas has successfully loaded.</summary>
    public static bool MigrateLegacyAppearance(PetSnapshot snapshot)
    {
        if (snapshot.AssetId != LegacyAssetId) return false;
        snapshot.AssetId = DefaultAssetId;
        snapshot.BreedId = DefaultBreedId;
        snapshot.CoatId = DefaultCoatId;
        return true;
    }
}
