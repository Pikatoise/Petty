using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Petty.Server.Application.Features.Letters;
using Petty.Server.Domain.Letters;
using Petty.Server.Infrastructure.Persistence;

namespace Petty.Server.Tests;

[Collection("PostgreSQL")]
public sealed class LettersApiTests(PostgreSqlDatabase database)
{
    [PostgreSqlFact]
    public async Task ConcurrentTenthSendAndSnapshotReadKeepQuotaAtomic()
    {
        using TestUser sender = await database.CreateUserAsync();
        using TestUser recipient = await database.CreateUserAsync();
        await Befriend(sender, recipient);
        var original = new List<SentLetter>();
        for (int index = 0; index < 9; index++)
        {
            original.Add(await Send(sender, recipient, $"Letter {index}"));
        }

        HttpResponseMessage[] contenders = await Task.WhenAll(
            SendRaw(sender, recipient, "Contender A"), SendRaw(sender, recipient, "Contender B"));
        SentLetter winner;
        try
        {
            HttpResponseMessage successful = Assert.Single(contenders, response => response.StatusCode == HttpStatusCode.Created);
            winner = (await successful.Content.ReadFromJsonAsync<SentLetter>())!;
            HttpResponseMessage rejected = Assert.Single(contenders, response => response.StatusCode == HttpStatusCode.Conflict);
            JsonElement problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("UNREAD_MAIL_LIMIT_REACHED", problem.GetProperty("code").GetString());
            Assert.Equal(10, problem.GetProperty("unreadCount").GetInt32());
            Assert.Equal(10, problem.GetProperty("limit").GetInt32());
        }
        finally
        {
            foreach (HttpResponseMessage response in contenders)
            {
                response.Dispose();
            }
        }

        Assert.Equal(10, (await Quota(sender, recipient)).UnreadCount);
        Assert.Empty((await Page(recipient, incoming: true)).Items);
        ReadLettersResult read = await Read(recipient, [original[0].LetterId]);
        Assert.Equal(9, read.UnreadCount);
        ReadLettersResult repeated = await Read(recipient, [original[0].LetterId]);
        Assert.Equal(Assert.Single(read.Letters).ReadAtUtc, Assert.Single(repeated.Letters).ReadAtUtc);
        SentLetter later = await Send(sender, recipient, "Arrived after the visit snapshot");
        Guid[] snapshot = original.Skip(1).Select(letter => letter.LetterId).Append(winner.LetterId).ToArray();
        ReadLettersResult all = await Read(recipient, snapshot);
        Assert.Equal(9, all.Letters.Count);
        Assert.Equal(1, all.UnreadCount);
        Assert.Equal(1, (await Quota(sender, recipient)).UnreadCount);
        using IServiceScope scope = database.Factory.Services.CreateScope();
        ServerDbContext context = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        Assert.Null((await context.Letters.SingleAsync(letter => letter.Id == later.LetterId)).ReadAtUtc);
        Assert.Equal(11, await context.Letters.CountAsync(letter => letter.SenderId == sender.Id));
        Assert.Equal(10, (await Walk(recipient, incoming: true)).Count);
        Assert.Equal(11, (await Walk(sender, incoming: false)).Count);
        await Read(recipient, [later.LetterId]);
        Assert.Equal(11, (await Walk(recipient, incoming: true)).Count);
    }

    [PostgreSqlFact]
    public async Task RetriesAndConcurrentKeysAreScopedToSenderAndImmutablePayload()
    {
        using TestUser first = await database.CreateUserAsync();
        using TestUser second = await database.CreateUserAsync();
        using TestUser third = await database.CreateUserAsync();
        await Befriend(first, second);
        await Befriend(first, third);
        await Befriend(third, second);
        var key = Guid.NewGuid();
        SentLetter initial = await Send(first, second, "Exact original text", key);
        SentLetter retry = await Send(first, second, "Exact original text", key);
        Assert.Equal(initial.LetterId, retry.LetterId);
        Assert.Equal(initial.SentAtUtc, retry.SentAtUtc);
        using HttpResponseMessage changedBody = await SendRaw(first, second, "Changed text", key);
        Assert.Equal(HttpStatusCode.Conflict, changedBody.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", await ErrorCode(changedBody));
        using HttpResponseMessage changedRecipient = await SendRaw(first, third, "Exact original text", key);
        Assert.Equal(HttpStatusCode.Conflict, changedRecipient.StatusCode);
        await Send(third, second, "Same key, another sender", key);
        Assert.Equal(1, (await Quota(first, second)).UnreadCount);
        Assert.Equal(1, (await Quota(third, second)).UnreadCount);
        Assert.Equal(0, (await Quota(second, first)).UnreadCount);

        var duplicateKey = Guid.NewGuid();
        HttpResponseMessage[] duplicates = await Task.WhenAll(
            SendRaw(first, second, "Same operation", duplicateKey), SendRaw(first, second, "Same operation", duplicateKey));
        try
        {
            Assert.All(duplicates, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            SentLetter left = (await duplicates[0].Content.ReadFromJsonAsync<SentLetter>())!;
            SentLetter right = (await duplicates[1].Content.ReadFromJsonAsync<SentLetter>())!;
            Assert.Equal(left.LetterId, right.LetterId);
            Assert.Equal(2, (await Quota(first, second)).UnreadCount);
        }
        finally
        {
            foreach (HttpResponseMessage response in duplicates)
            {
                response.Dispose();
            }
        }

        var crossRecipientKey = Guid.NewGuid();
        HttpResponseMessage[] reused = await Task.WhenAll(
            SendRaw(first, second, "One key across recipients", crossRecipientKey),
            SendRaw(first, third, "One key across recipients", crossRecipientKey));
        try
        {
            Assert.Single(reused, response => response.StatusCode == HttpStatusCode.Created);
            HttpResponseMessage conflict = Assert.Single(reused, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("IDEMPOTENCY_KEY_REUSED", await ErrorCode(conflict));
        }
        finally
        {
            foreach (HttpResponseMessage response in reused)
            {
                response.Dispose();
            }
        }
    }

    [PostgreSqlFact]
    public async Task RemovalAndBlockingPreserveUnreadQuotaAndReadHistory()
    {
        using TestUser sender = await database.CreateUserAsync();
        using TestUser recipient = await database.CreateUserAsync();
        await Befriend(sender, recipient);
        var key = Guid.NewGuid();
        SentLetter first = await Send(sender, recipient, "Already read", key);
        SentLetter second = await Send(sender, recipient, "Still unread");
        await Read(recipient, [first.LetterId]);
        using HttpResponseMessage removed = await sender.Client.DeleteAsync($"/api/v1/friends/{recipient.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using HttpResponseMessage forbiddenSend = await SendRaw(sender, recipient, "No friendship");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenSend.StatusCode);
        using HttpResponseMessage forbiddenRead = await recipient.Client.PostAsync($"/api/v1/letters/{second.LetterId}/read", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRead.StatusCode);
        Assert.Equal(1, (await Quota(sender, recipient)).UnreadCount);
        Assert.Equal(2, (await Walk(sender, incoming: false)).Count);
        Assert.Single((await Walk(recipient, incoming: true)));
        Assert.Equal(first.LetterId, (await Send(sender, recipient, "Already read", key)).LetterId);
        using HttpResponseMessage blocked = await sender.Client.PostAsync($"/api/v1/users/{recipient.Id}/block", null);
        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);
        Assert.Equal(1, (await Quota(sender, recipient)).UnreadCount);
        Assert.Single((await Walk(recipient, incoming: true)));
        using HttpResponseMessage unblocked = await sender.Client.DeleteAsync($"/api/v1/users/{recipient.Id}/block");
        Assert.Equal(HttpStatusCode.NoContent, unblocked.StatusCode);
        await Befriend(sender, recipient);
        Assert.Equal(0, (await Read(recipient, [second.LetterId])).UnreadCount);
        Assert.Equal(2, (await Walk(recipient, incoming: true)).Count);
    }

    [PostgreSqlFact]
    public async Task KeysetHistoryHandlesTimestampTiesAndKeepsUnreadPrivate()
    {
        using TestUser first = await database.CreateUserAsync();
        using TestUser second = await database.CreateUserAsync();
        using TestUser third = await database.CreateUserAsync();
        await Befriend(first, second);
        await Befriend(first, third);
        await Befriend(third, second);
        var toSecond = new List<SentLetter>();
        for (int index = 0; index < 6; index++)
        {
            toSecond.Add(await Send(first, second, $"Private {index}"));
        }
        await Send(first, third, "Third's first letter");
        await Send(first, third, "Third's second letter");
        SentLetter otherSender = await Send(third, second, "Another sender");
        await Read(second, toSecond.Take(4).Select(letter => letter.LetterId).ToArray());
        await Read(second, [otherSender.LetterId]);
        using IServiceScope scope = database.Factory.Services.CreateScope();
        ServerDbContext context = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds());
        await context.Letters.Where(letter => letter.SenderId == first.Id || letter.SenderId == third.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(letter => letter.SentAtUtc, timestamp));
        await context.Letters.Where(letter => letter.RecipientId == second.Id && letter.ReadAtUtc != null)
            .ExecuteUpdateAsync(update => update.SetProperty(letter => letter.ReadAtUtc, (DateTimeOffset?)timestamp.AddMinutes(1)));

        LetterHistoryPage head = await Page(first, incoming: false);
        Assert.Equal(2, head.Items.Count);
        Assert.NotNull(head.NextCursor);
        SentLetter newlyArrived = await Send(first, second, "New top item");
        List<LetterHistoryItem> remaining = await Walk(first, incoming: false, head.NextCursor);
        LetterHistoryItem[] original = head.Items.Concat(remaining).ToArray();
        Assert.Equal(8, original.Length);
        Assert.Equal(8, original.Select(letter => letter.Id).Distinct().Count());
        Assert.DoesNotContain(original, letter => letter.Id == newlyArrived.LetterId);
        Assert.All(original, letter => Assert.Equal(first.Id, letter.SenderId));
        Assert.Equal(original.Select(letter => letter.Id).OrderByDescending(id => id), original.Select(letter => letter.Id));
        List<LetterHistoryItem> incoming = await Walk(second, incoming: true);
        Assert.Equal(5, incoming.Count);
        Assert.Equal(5, incoming.Select(letter => letter.Id).Distinct().Count());
        Assert.All(incoming, letter => Assert.NotNull(letter.ReadAtUtc));
        Assert.Empty((await Walk(first, incoming: true)));
        using HttpResponseMessage invalidCursor = await first.Client.GetAsync("/api/v1/letters/outgoing?cursor=not-a-cursor");
        Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
        using HttpResponseMessage invalidLimit = await first.Client.GetAsync("/api/v1/letters/incoming?limit=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidLimit.StatusCode);
    }

    [PostgreSqlFact]
    public async Task ReadListsRejectForeignIdsMixedSendersAndDuplicatesWithoutPartialChanges()
    {
        using TestUser first = await database.CreateUserAsync();
        using TestUser second = await database.CreateUserAsync();
        using TestUser third = await database.CreateUserAsync();
        await Befriend(first, second);
        await Befriend(third, second);
        SentLetter owned = await Send(first, second, "Owned by second");
        SentLetter anotherSender = await Send(third, second, "Also owned by second");
        SentLetter foreign = await Send(second, first, "Owned by first");
        using HttpResponseMessage wrongRecipient = await first.Client.PostAsync($"/api/v1/letters/{owned.LetterId}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, wrongRecipient.StatusCode);
        using HttpResponseMessage partlyForeign = await second.Client.PostAsJsonAsync("/api/v1/letters/read", new
        {
            letterIds = new[] { owned.LetterId, foreign.LetterId }
        });
        Assert.Equal(HttpStatusCode.NotFound, partlyForeign.StatusCode);
        using HttpResponseMessage mixed = await second.Client.PostAsJsonAsync("/api/v1/letters/read", new
        {
            letterIds = new[] { owned.LetterId, anotherSender.LetterId }
        });
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
        using HttpResponseMessage duplicate = await second.Client.PostAsJsonAsync("/api/v1/letters/read", new
        {
            letterIds = new[] { owned.LetterId, owned.LetterId }
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(1, (await Quota(first, second)).UnreadCount);
        Assert.Equal(1, (await Quota(third, second)).UnreadCount);
        Assert.Empty((await Walk(second, incoming: true)));
    }

    [PostgreSqlFact]
    public async Task ConcurrentBlockingAndSendHaveOneConsistentOutcome()
    {
        using TestUser sender = await database.CreateUserAsync();
        using TestUser recipient = await database.CreateUserAsync();
        await Befriend(sender, recipient);
        HttpResponseMessage[] responses = await Task.WhenAll(SendRaw(sender, recipient, "Racing with blocking"),
            recipient.Client.PostAsync($"/api/v1/users/{sender.Id}/block", null));
        bool sent;
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, responses[1].StatusCode);
            Assert.Contains(responses[0].StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Forbidden });
            sent = responses[0].StatusCode == HttpStatusCode.Created;
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
        Assert.Equal(sent ? 1 : 0, (await Quota(sender, recipient)).UnreadCount);
        Assert.Equal(sent ? 1 : 0, (await Walk(sender, incoming: false)).Count);
        using HttpResponseMessage afterBlock = await SendRaw(sender, recipient, "Cannot send after block");
        Assert.Equal(HttpStatusCode.Forbidden, afterBlock.StatusCode);
        Assert.Empty((await Walk(recipient, incoming: true)));
    }

    [PostgreSqlFact]
    public async Task UnicodeScalarLimitRoundTripsWithoutTruncationAndRejectsControls()
    {
        using TestUser sender = await database.CreateUserAsync();
        using TestUser recipient = await database.CreateUserAsync();
        await Befriend(sender, recipient);
        string body = string.Concat(Enumerable.Repeat("🐾", 500));
        await Send(sender, recipient, body);
        LetterHistoryItem item = Assert.Single((await Page(sender, incoming: false)).Items);
        Assert.Equal(body, item.Body);
        foreach (string invalid in new[] { body + "🐾", " \r\n\t", "bad\0text", "bad\u001btext" })
        {
            using HttpResponseMessage rejected = await SendRaw(sender, recipient, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        const string plainText = "<b>Plain text</b>\nA new line\tand a tab";
        await Send(sender, recipient, plainText);
        Assert.Contains((await Page(sender, incoming: false)).Items, letter => letter.Body == plainText);
        using HttpResponseMessage history = await sender.Client.GetAsync("/api/v1/letters/outgoing");
        Assert.True(history.Headers.CacheControl?.NoStore);
        using HttpClient anonymous = database.Factory.CreateClient();
        using HttpResponseMessage unauthenticated = await anonymous.GetAsync("/api/v1/letters/outgoing");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [PostgreSqlFact]
    public async Task SendRateLimitDoesNotBlockReadingOrQuotaQueries()
    {
        using TestUser sender = await database.CreateUserAsync();
        using TestUser recipient = await database.CreateUserAsync();
        await Befriend(sender, recipient);
        using WebApplicationFactory<Program> limitedFactory = database.Factory.WithWebHostBuilder(builder => builder.UseSetting("Letters:RequestsPerMinute", "1"));
        using HttpClient client = limitedFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = sender.Client.DefaultRequestHeaders.Authorization;
        using HttpResponseMessage first = await client.PostAsJsonAsync("/api/v1/letters", new
        {
            recipientUserId = recipient.Id,
            body = "First",
            clientRequestId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using HttpResponseMessage second = await client.PostAsJsonAsync("/api/v1/letters", new
        {
            recipientUserId = recipient.Id,
            body = "Second",
            clientRequestId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        using HttpResponseMessage quota = await client.GetAsync($"/api/v1/letters/quota/{recipient.Id}");
        Assert.Equal(HttpStatusCode.OK, quota.StatusCode);
        using HttpResponseMessage history = await client.GetAsync("/api/v1/letters/outgoing");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
    }

    [Fact]
    public void BodyValidationRejectsMalformedUtf16()
    {
        Assert.False(LetterRules.IsValidBody("bad\ud800"));
        Assert.False(LetterRules.IsValidBody("bad\udc00"));
    }

    private static async Task Befriend(TestUser first, TestUser second)
    {
        using HttpResponseMessage requested = await first.Client.PostAsJsonAsync("/api/v1/friends/requests", new
        {
            login = second.Login
        });
        Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
        Guid id = (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetGuid();
        using HttpResponseMessage accepted = await second.Client.PostAsync($"/api/v1/friends/requests/{id}/accept", null);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    private static Task<HttpResponseMessage> SendRaw(TestUser sender, TestUser recipient, string body, Guid? key = null) =>
        sender.Client.PostAsJsonAsync("/api/v1/letters", new
        {
            recipientUserId = recipient.Id,
            body,
            clientRequestId = key ?? Guid.NewGuid(),
            senderId = Guid.NewGuid()
        });

    private static async Task<SentLetter> Send(TestUser sender, TestUser recipient, string body, Guid? key = null)
    {
        using HttpResponseMessage response = await SendRaw(sender, recipient, body, key);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SentLetter>())!;
    }

    private static async Task<ReadLettersResult> Read(TestUser recipient, Guid[] ids)
    {
        using HttpResponseMessage response = ids.Length == 1
            ? await recipient.Client.PostAsync($"/api/v1/letters/{ids[0]}/read", null)
            : await recipient.Client.PostAsJsonAsync("/api/v1/letters/read", new
            {
                letterIds = ids
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReadLettersResult>())!;
    }

    private static async Task<MailQuota> Quota(TestUser sender, TestUser recipient) =>
        (await sender.Client.GetFromJsonAsync<MailQuota>($"/api/v1/letters/quota/{recipient.Id}"))!;

    private static async Task<LetterHistoryPage> Page(TestUser user, bool incoming, string? cursor = null) =>
        (await user.Client.GetFromJsonAsync<LetterHistoryPage>($"/api/v1/letters/{(incoming ? "incoming" : "outgoing")}?limit=2&cursor={Uri.EscapeDataString(cursor ?? string.Empty)}"))!;

    private static async Task<List<LetterHistoryItem>> Walk(TestUser user, bool incoming, string? cursor = null)
    {
        var items = new List<LetterHistoryItem>();
        do
        {
            LetterHistoryPage page = await Page(user, incoming, cursor);
            items.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return items;
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
}
