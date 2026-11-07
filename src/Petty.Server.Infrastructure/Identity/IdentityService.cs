using Microsoft.AspNetCore.Identity;
using Petty.Server.Application;
using Petty.Server.Application.Features.Auth;

namespace Petty.Server.Infrastructure.Identity;

public sealed class IdentityService(UserManager<ApplicationUser> users) : IIdentityService
{
    public async Task<Guid> RegisterAsync(string login, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = login };
        IdentityResult result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            bool duplicate = result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.DuplicateUserName));
            throw new RequestFailure(duplicate ? FailureKind.Conflict : FailureKind.Invalid,
                duplicate ? "USERNAME_TAKEN" : "INVALID_REGISTRATION");
        }

        return user.Id;
    }

    public async Task<Guid?> VerifyPasswordAsync(string login, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationUser? user = await users.FindByNameAsync(login);
        return user is not null && await users.CheckPasswordAsync(user, password) ? user.Id : null;
    }
}
