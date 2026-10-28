namespace Petty.Core.Domain;

/// <summary>
/// Fixed prototype content. Additional breeds and coats become extra entries here
/// (plus assets) without touching the runtime code.
/// </summary>
public static class PetCatalog
{
    public const string DefaultBreedId = "cat";
    public const string DefaultCoatId = "orange-white-01";
    public const string DefaultAssetId = "cat-orange-white";
}
