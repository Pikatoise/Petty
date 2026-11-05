using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Petty.Server.Infrastructure.Persistence;

namespace Petty.Server.Infrastructure.Identity;

public sealed class ApplicationUserStore : UserOnlyStore<ApplicationUser, ServerDbContext, Guid>
{
    public ApplicationUserStore(ServerDbContext context, IdentityErrorDescriber errors) : base(context, errors)
    {
        AutoSaveChanges = false;
    }
}
