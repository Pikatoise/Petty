using Petty.Core.Domain;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Petty.Tests;

public sealed class PetCatalogTests
{
    [Fact]
    public void RetiredAppearanceMigrationPreservesAllOtherStateAndIsIdempotent()
    {
        var snapshot = new PetSnapshot
        {
            AssetId = "cat-orange-white", BreedId = "cat", CoatId = "orange-white-01",
            PetName = "Milo", Satiety = 23, Happiness = 47, BowlFood = 61,
            BowlRelativeX = 0.37, BowlBottomOffset = 89, DisplayDeviceName = "DISPLAY2",
            CreatedUtc = DateTimeOffset.UtcNow.AddDays(-3), LastUpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        };
        var expected = JsonSerializer.SerializeToNode(snapshot)!;
        expected[nameof(PetSnapshot.AssetId)] = PetCatalog.DefaultAssetId;
        expected[nameof(PetSnapshot.BreedId)] = PetCatalog.DefaultBreedId;
        expected[nameof(PetSnapshot.CoatId)] = PetCatalog.DefaultCoatId;
        Assert.True(PetCatalog.MigrateLegacyAppearance(snapshot));
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(snapshot)));
        Assert.False(PetCatalog.MigrateLegacyAppearance(snapshot));
    }

    [Fact]
    public void OtherSavedAppearanceIsNeverReplacedByMigration()
    {
        var snapshot = new PetSnapshot { AssetId = "custom-cat", BreedId = "custom", CoatId = "tabby" };
        var before = JsonSerializer.Serialize(snapshot);
        Assert.False(PetCatalog.MigrateLegacyAppearance(snapshot));
        Assert.Equal(before, JsonSerializer.Serialize(snapshot));
    }
}
