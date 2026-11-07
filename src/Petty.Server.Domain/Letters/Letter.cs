namespace Petty.Server.Domain.Letters;

public sealed class Letter
{
    public Guid Id
    {
        get; init;
    }
    public Guid SenderId
    {
        get; init;
    }
    public Guid RecipientId
    {
        get; init;
    }
    public required string Body
    {
        get; init;
    }
    public DateTimeOffset SentAtUtc
    {
        get; init;
    }
    public DateTimeOffset? ReadAtUtc
    {
        get; set;
    }
    public Guid ClientRequestId
    {
        get; init;
    }
}
