using Microsoft.EntityFrameworkCore;
using Petty.Server.Application.Features.Delivery;
using Petty.Server.Application.Features.Friends;
using Petty.Server.Domain.Friends;
using Petty.Server.Domain.Letters;

namespace Petty.Server.Infrastructure.Persistence;

public sealed class MailboxRepository(ServerDbContext database) : IMailboxRepository
{
    public async Task<MailboxVisit?> GetNextAsync(Guid userId, CancellationToken cancellationToken)
    {
        IQueryable<Letter> available = database.Letters.AsNoTracking().Where(letter => letter.RecipientId == userId && letter.ReadAtUtc == null
            && database.Friendships.Any(friendship => friendship.Status == FriendshipStatus.Accepted
                && ((friendship.LowerUserId == userId && friendship.HigherUserId == letter.SenderId)
                    || (friendship.HigherUserId == userId && friendship.LowerUserId == letter.SenderId)))
            && !database.UserBlocks.Any(block => (block.BlockerId == userId && block.BlockedId == letter.SenderId)
                || (block.BlockerId == letter.SenderId && block.BlockedId == userId)));
        IQueryable<Guid> firstSender = available.OrderBy(letter => letter.SentAtUtc).ThenBy(letter => letter.Id)
            .Select(letter => letter.SenderId).Take(1);
        // One SQL statement fixes sender selection and its letters to the same database snapshot.
        var rows = await (from letter in available
                          where firstSender.Contains(letter.SenderId)
                          join sender in database.Users.AsNoTracking() on letter.SenderId equals sender.Id
                          orderby letter.SentAtUtc, letter.Id
                          select new
                          {
                              Sender = new FriendDto(sender.Id, sender.UserName!, sender.PetName, sender.PetAssetId),
                              Letter = new DeliveryLetter(letter.Id, letter.Body, letter.SentAtUtc)
                          }).Take(LetterRules.UnreadLimit).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : new MailboxVisit(rows[0].Sender, rows.Select(row => row.Letter).ToArray());
    }
}
