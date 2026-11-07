namespace Petty.Server.Domain.Accounts;

public sealed class UserSession
{
    public Guid Id
    {
        get; init;
    }
    public Guid UserId
    {
        get; init;
    }
    public DateTimeOffset CreatedAtUtc
    {
        get; init;
    }
    public DateTimeOffset RefreshExpiresAtUtc
    {
        get; set;
    }
    public DateTimeOffset? RevokedAtUtc
    {
        get; set;
    }
    public required string RefreshTokenHash
    {
        get; set;
    }

    public bool IsActive(DateTimeOffset now) => RevokedAtUtc is null && RefreshExpiresAtUtc > now;
}
