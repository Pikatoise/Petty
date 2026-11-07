using System.Net;
using System.Text.Json;
using Petty.App.Online;
using Petty.Online.Client;
using static Petty.Online.Tests.OnlineClientTests;

namespace Petty.Online.Tests;

public sealed class LetterUiStateTests
{
    [Fact]
    public void DraftFreezesPayloadAndKeyUntilAcknowledgement()
    {
        var draft = new LetterDraft();
        SendLetter original = draft.Prepare(Guid.NewGuid(), "First letter");
        Assert.Same(original, draft.Prepare(Guid.NewGuid(), "Changed text"));
        Assert.False(draft.Sent);
        draft.Confirm(new SentLetter(Guid.NewGuid(), DateTimeOffset.UtcNow, 10, 10));
        Assert.True(draft.Sent);
        Assert.Throws<InvalidOperationException>(() => draft.Prepare(original.RecipientUserId, original.Body));
        Assert.NotEqual(original.ClientRequestId, new LetterDraft().Prepare(original.RecipientUserId, original.Body).ClientRequestId);
        var rejected = new LetterDraft();
        SendLetter rejectedRequest = rejected.Prepare(original.RecipientUserId, original.Body);
        rejected.Reject();
        Assert.Null(rejected.Pending);
        Assert.NotEqual(rejectedRequest.ClientRequestId, rejected.Prepare(original.RecipientUserId, "Edited after rejection").ClientRequestId);
    }

    [Fact]
    public void BodyValidationCountsScalarsAndRejectsBrokenUtf16AndControls()
    {
        Assert.True(LetterDraft.IsValidBody(string.Concat(Enumerable.Repeat("🐈", 500))));
        Assert.False(LetterDraft.IsValidBody(string.Concat(Enumerable.Repeat("🐈", 501))));
        Assert.True(LetterDraft.IsValidBody("Hello\n\tworld\r\n"));
        Assert.False(LetterDraft.IsValidBody(" \n\t"));
        Assert.False(LetterDraft.IsValidBody("Hello\0"));
        Assert.False(LetterDraft.IsValidBody("Hello\uD800"));
    }

    [Fact]
    public async Task VisitKeepsSnapshotRetriesOnlyPendingAndRequiresServerAcknowledgement()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var source = new List<DeliveryLetter>
        {
            new(first, "First", DateTimeOffset.UtcNow), new(second, "Second", DateTimeOffset.UtcNow)
        };
        var visit = new IncomingVisit(new MailboxVisit(new Friend(Guid.NewGuid(), "friend", null, null), source));
        source.Add(new DeliveryLetter(Guid.NewGuid(), "Later arrival", DateTimeOffset.UtcNow));
        Assert.Equal(new[] { first, second }, visit.UnreadIds);
        bool offline = true;
        bool incomplete = false;
        var requests = new List<Guid[]>();
        await using var client = new OnlineClient(new Uri("https://example.test"), new MemoryStore { Tokens = Tokens() },
            new Handler(async (request, ct) =>
            {
                using JsonDocument json = await JsonDocument.ParseAsync(await request.Content!.ReadAsStreamAsync(ct), cancellationToken: ct);
                Guid[] ids = json.RootElement.GetProperty("letterIds").EnumerateArray().Select(item => item.GetGuid()).ToArray();
                requests.Add(ids);
                return offline ? Json(new
                {
                    code = "REQUEST_FAILED"
                }, HttpStatusCode.ServiceUnavailable) :
                    Json(new ReadLettersResult(incomplete ? [] : ids.Select(id => new ReadLetter(id, DateTimeOffset.UtcNow)).ToArray(), 0, 10));
            }));
        await client.RestoreAsync();
        await Assert.ThrowsAsync<OnlineException>(() => visit.ReadAsync(client, [first], default));
        Assert.False(visit.IsRead(first));
        Assert.False(visit.Complete);
        offline = false;
        incomplete = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => visit.ReadAsync(client, [first], default));
        Assert.False(visit.IsRead(first));
        incomplete = false;
        await visit.ReadAsync(client, [first], default);
        Assert.True(visit.IsRead(first));
        int calls = requests.Count;
        await visit.ReadAsync(client, [first], default);
        Assert.Equal(calls, requests.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => visit.ReadAsync(client, [source[2].Id], default));
        await visit.ReadAsync(client, visit.UnreadIds, default);
        Assert.Equal(new[] { second }, requests[^1]);
        Assert.True(visit.Complete);
    }
}
