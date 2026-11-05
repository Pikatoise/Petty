using Mediator;
using Petty.Server.Application.Features.Friends;

namespace Petty.Server.Application.Features.Delivery;

public sealed record DeliveryLetter(Guid Id, string Body, DateTimeOffset SentAtUtc);
public sealed record MailboxVisit(FriendDto Sender, IReadOnlyList<DeliveryLetter> Letters);

public interface IMailboxRepository
{
    Task<MailboxVisit?> GetNextAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record GetNextVisit(Guid UserId) : IQuery<MailboxVisit?>;

public sealed class GetNextVisitHandler(IMailboxRepository mailbox) : IQueryHandler<GetNextVisit, MailboxVisit?>
{
    public async ValueTask<MailboxVisit?> Handle(GetNextVisit query, CancellationToken cancellationToken) =>
        await mailbox.GetNextAsync(query.UserId, cancellationToken);
}
