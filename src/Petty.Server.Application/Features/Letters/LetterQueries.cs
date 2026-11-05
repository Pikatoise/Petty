using System.Globalization;
using System.Text;
using Mediator;

namespace Petty.Server.Application.Features.Letters;

public sealed record GetMailQuota(Guid UserId, Guid RecipientUserId) : IQuery<MailQuota>;

public sealed class GetMailQuotaHandler(ILetterRepository letters) : IQueryHandler<GetMailQuota, MailQuota>
{
    public async ValueTask<MailQuota> Handle(GetMailQuota query, CancellationToken cancellationToken) =>
        new(await letters.CountUnreadAsync(query.UserId, query.RecipientUserId, cancellationToken));
}

public sealed record GetLetterHistory(Guid UserId, bool Incoming, string? Cursor, int Limit) : IQuery<LetterHistoryPage>;

public sealed class GetLetterHistoryHandler(ILetterRepository letters) : IQueryHandler<GetLetterHistory, LetterHistoryPage>
{
    public async ValueTask<LetterHistoryPage> Handle(GetLetterHistory query, CancellationToken cancellationToken)
    {
        if (query.Limit is < 1 or > 100)
        {
            throw new RequestFailure(FailureKind.Invalid, "INVALID_PAGE_LIMIT");
        }

        LetterCursor? cursor = DecodeCursor(query.Cursor);
        IReadOnlyList<LetterHistoryItem> fetched = await letters.GetHistoryAsync(query.UserId, query.Incoming, cursor, query.Limit + 1, cancellationToken);
        LetterHistoryItem[] items = fetched.Take(query.Limit).ToArray();
        string? next = null;
        if (fetched.Count > query.Limit)
        {
            LetterHistoryItem last = items[^1];
            DateTimeOffset timestamp = query.Incoming ? last.ReadAtUtc!.Value : last.SentAtUtc;
            next = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{timestamp.ToString("O", CultureInfo.InvariantCulture)}|{last.Id:N}"))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        return new LetterHistoryPage(items, next);
    }

    private static LetterCursor? DecodeCursor(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Length <= 128)
        {
            try
            {
                string encoded = value.Replace('-', '+').Replace('_', '/');
                string[] parts = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='))).Split('|');
                if (parts.Length == 2 && DateTimeOffset.TryParseExact(parts[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset time)
                    && time.Offset == TimeSpan.Zero && Guid.TryParseExact(parts[1], "N", out Guid id) && id != Guid.Empty)
                {
                    return new LetterCursor(time, id);
                }
            }
            catch (FormatException)
            {
            }
        }

        throw new RequestFailure(FailureKind.Invalid, "INVALID_CURSOR");
    }
}
