using System.Collections.Frozen;

namespace Petty.Server.Domain.Accounts;

public static class PetProfileRules
{
    // IDs only: the server neither builds nor loads Desktop artwork.
    public static readonly FrozenSet<string> SupportedAssets = new[]
    {
        "british-shorthair-blue", "british-shorthair-blue-white", "british-shorthair-cream",
        "british-shorthair-silver-tabby", "british-shorthair-brown-spotted",
        "maine-coon-brown-tabby", "maine-coon-red-tabby-white", "maine-coon-black-smoke",
        "maine-coon-silver-classic", "maine-coon-tortoiseshell",
        "siamese-seal-point", "siamese-blue-point", "siamese-chocolate-point", "siamese-lilac-point", "siamese-lynx-point",
        "persian-white", "persian-cream", "persian-red-white", "persian-black-white", "persian-shaded-silver",
        "cardigan-corgi-red-white", "cardigan-corgi-sable-white", "cardigan-corgi-black-tricolor",
        "cardigan-corgi-blue-merle", "cardigan-corgi-brindle-white",
        "dachshund-red", "dachshund-black-tan", "dachshund-chocolate-tan", "dachshund-silver-dapple", "dachshund-brindle",
        "border-collie-black-white", "border-collie-red-white", "border-collie-blue-merle", "border-collie-red-merle", "border-collie-tricolor",
        "siberian-husky-black-white", "siberian-husky-gray-white", "siberian-husky-red-white", "siberian-husky-agouti", "siberian-husky-piebald"
    }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsValidName(string name) => name.Length is >= 1 and <= 24 && !name.Any(char.IsControl);
}
