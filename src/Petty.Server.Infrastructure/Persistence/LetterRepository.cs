using Microsoft.EntityFrameworkCore;
using Petty.Server.Application.Features.Friends;
using Petty.Server.Application.Features.Letters;
using Petty.Server.Domain.Letters;

namespace Petty.Server.Infrastructure.Persistence;

public sealed class LetterRepository(ServerDbContext database) : ILetterRepository
{
    public Task<Letter?> FindSentAsync(Guid senderId, Guid clientRequestId, CancellationToken cancellationToken) =>
        database.Letters.AsNoTracking().SingleOrDefaultAsync(letter => letter.SenderId == senderId && letter.ClientRequestId == clientRequestId, cancellationToken);

    public Task<int> CountUnreadAsync(Guid senderId, Guid recipientId, CancellationToken cancellationToken) =>
        database.Letters.CountAsync(letter => letter.SenderId == senderId && letter.RecipientId == recipientId && letter.ReadAtUtc == null, cancellationToken);

    public Task<Guid?> FindReceivedSenderAsync(Guid recipientId, Guid letterId, CancellationToken cancellationToken) =>
        database.Letters.Where(letter => letter.RecipientId == recipientId && letter.Id == letterId)
            .Select(letter => (Guid?)letter.SenderId).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Letter>> GetReceivedAsync(Guid recipientId, Guid[] letterIds, CancellationToken cancellationToken) =>
        await database.Letters.Where(letter => letter.RecipientId == recipientId && letterIds.Contains(letter.Id))
            .OrderBy(letter => letter.SentAtUtc).ThenBy(letter => letter.Id).ToListAsync(cancellationToken);

    public void Add(Letter letter) => database.Letters.Add(letter);

    public async Task<IReadOnlyList<LetterHistoryItem>> GetHistoryAsync(Guid userId, bool incoming, LetterCursor? cursor, int count,
        CancellationToken cancellationToken)
    {
        IQueryable<Letter> history = database.Letters.AsNoTracking();
        if (incoming)
        {
            history = history.Where(letter => letter.RecipientId == userId && letter.ReadAtUtc != null);
            if (cursor is not null)
            {
                history = history.Where(letter => EF.Functions.LessThan(ValueTuple.Create(letter.ReadAtUtc!.Value, letter.Id),
                    ValueTuple.Create(cursor.Timestamp, cursor.LetterId)));
            }

            history = history.OrderByDescending(letter => letter.ReadAtUtc).ThenByDescending(letter => letter.Id);
        }
        else
        {
            history = history.Where(letter => letter.SenderId == userId);
            if (cursor is not null)
            {
                history = history.Where(letter => EF.Functions.LessThan(ValueTuple.Create(letter.SentAtUtc, letter.Id),
                    ValueTuple.Create(cursor.Timestamp, cursor.LetterId)));
            }

            history = history.OrderByDescending(letter => letter.SentAtUtc).ThenByDescending(letter => letter.Id);
        }

        return await (from letter in history.Take(count)
                      join other in database.Users.AsNoTracking() on (incoming ? letter.SenderId : letter.RecipientId) equals other.Id
                      select new LetterHistoryItem(letter.Id, letter.SenderId, letter.RecipientId, letter.Body, letter.SentAtUtc,
                          letter.ReadAtUtc, new FriendDto(other.Id, other.UserName!, other.PetName, other.PetAssetId))).ToListAsync(cancellationToken);
    }
}
