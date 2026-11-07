using Microsoft.AspNetCore.Identity;

namespace Petty.Server.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? PetName
    {
        get; set;
    }
    public string? PetAssetId
    {
        get; set;
    }
    public DateTimeOffset? ProfileUpdatedUtc
    {
        get; set;
    }
}
