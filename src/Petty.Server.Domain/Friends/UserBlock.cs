namespace Petty.Server.Domain.Friends;

public sealed class UserBlock
{
    public Guid BlockerId
    {
        get; init;
    }
    public Guid BlockedId
    {
        get; init;
    }
    public DateTimeOffset CreatedAtUtc
    {
        get; init;
    }
}
