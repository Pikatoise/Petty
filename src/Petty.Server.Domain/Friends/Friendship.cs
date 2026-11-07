namespace Petty.Server.Domain.Friends;

public enum FriendshipStatus
{
    Removed,
    Pending,
    Accepted
}

public sealed class Friendship
{
    public Guid Id
    {
        get; init;
    }
    public Guid LowerUserId
    {
        get; init;
    }
    public Guid HigherUserId
    {
        get; init;
    }
    public Guid RequestedById
    {
        get; set;
    }
    public Guid? RequestId
    {
        get; set;
    }
    public FriendshipStatus Status
    {
        get; set;
    }
    public DateTimeOffset CreatedAtUtc
    {
        get; init;
    }
    public DateTimeOffset UpdatedAtUtc
    {
        get; set;
    }

    public static (Guid Lower, Guid Higher) OrderPair(Guid first, Guid second) =>
        first.CompareTo(second) < 0 ? (first, second) : (second, first);
}
