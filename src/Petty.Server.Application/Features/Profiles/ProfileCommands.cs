using Mediator;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Application.Persistence;
using Petty.Server.Domain.Accounts;

namespace Petty.Server.Application.Features.Profiles;

public sealed record GetProfile(Guid UserId) : IQuery<AccountProfile>;

public sealed class GetProfileHandler(IAccountRepository accounts) : IQueryHandler<GetProfile, AccountProfile>
{
    public async ValueTask<AccountProfile> Handle(GetProfile query, CancellationToken cancellationToken) =>
        await accounts.GetProfileAsync(query.UserId, cancellationToken);
}

public sealed record UpdatePet(Guid UserId, string PetName, string AssetId) : ICommand<Unit>;

public sealed class UpdatePetHandler(IAccountRepository accounts, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<UpdatePet, Unit>
{
    public async ValueTask<Unit> Handle(UpdatePet command, CancellationToken cancellationToken)
    {
        string name = (command.PetName ?? string.Empty).Trim();
        if (!PetProfileRules.IsValidName(name) || !PetProfileRules.SupportedAssets.Contains(command.AssetId))
        {
            throw new RequestFailure(FailureKind.Invalid, "INVALID_PET_PROFILE");
        }

        await accounts.UpdatePetAsync(command.UserId, name, command.AssetId, clock.GetUtcNow(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
