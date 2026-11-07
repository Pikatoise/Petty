using Petty.Online.Client;

namespace Petty.App.Online;

/// <summary>In-memory visit snapshot. Later mailbox refreshes cannot change its read-all IDs.</summary>
internal sealed class IncomingVisit(MailboxVisit snapshot)
{
    private readonly Dictionary<Guid, DateTimeOffset> _read = [];
    public MailboxVisit Snapshot { get; } = snapshot with { Letters = snapshot.Letters.ToArray() };
    public Guid[] UnreadIds => Snapshot.Letters.Where(letter => !_read.ContainsKey(letter.Id)).Select(letter => letter.Id).ToArray();
    public bool Complete => UnreadIds.Length == 0;
    public bool IsRead(Guid id) => _read.ContainsKey(id);

    public async Task ReadAsync(OnlineClient client, Guid[] ids, CancellationToken ct)
    {
        Guid[] pending = ids.Distinct().Where(id => !IsRead(id)).ToArray();
        if (pending.Any(id => !Snapshot.Letters.Any(letter => letter.Id == id)))
        {
            throw new ArgumentException("Only the current visit can be acknowledged.", nameof(ids));
        }
        if (pending.Length == 0)
        {
            return;
        }
        ReadLettersResult result = await client.ReadLettersAsync(pending, ct);
        ct.ThrowIfCancellationRequested();
        foreach (ReadLetter letter in result.Letters.Where(letter => pending.Contains(letter.LetterId)))
        {
            _read.TryAdd(letter.LetterId, letter.ReadAtUtc);
        }
        if (pending.Any(id => !IsRead(id)))
        {
            throw new InvalidOperationException("Incomplete read acknowledgement.");
        }
    }
}
