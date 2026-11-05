using Mediator;
using Petty.Server.Application.Persistence;

namespace Petty.Server.Application.Features.Auth;

public sealed record Register(string Login, string Password, string ConfirmPassword) : ICommand<Guid>;

public sealed class RegisterHandler(IIdentityService identity, IUnitOfWork unitOfWork) : ICommandHandler<Register, Guid>
{
    public async ValueTask<Guid> Handle(Register command, CancellationToken cancellationToken)
    {
        string login = NormalizeLogin(command.Login);
        if (command.Password is not { Length: >= 12 and <= 128 } || command.Password != command.ConfirmPassword)
        {
            throw new RequestFailure(FailureKind.Invalid, "INVALID_REGISTRATION");
        }

        Guid userId = await identity.RegisterAsync(login, command.Password, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return userId;
    }

    internal static string NormalizeLogin(string? input)
    {
        string login = (input ?? string.Empty).Trim();
        if (login.Length is < 3 or > 32 || login.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_'))
        {
            throw new RequestFailure(FailureKind.Invalid, "INVALID_LOGIN");
        }

        return login;
    }
}
