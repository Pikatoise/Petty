using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Petty.Server.Application.Features.Delivery;
using Petty.Server.Application.Features.Letters;
using Petty.Server.Infrastructure.Persistence;

namespace Petty.Server.Tests;

[Collection("PostgreSQL")]
public sealed class MailboxApiTests(PostgreSqlDatabase database)
{
    [PostgreSqlFact]
    public async Task EarliestLetterSelectsSenderAndSnapshotDoesNotConsumeNewArrivals()
    {
        using TestUser first = await database.CreateUserAsync();
        using TestUser second = await database.CreateUserAsync();
        using TestUser recipient = await database.CreateUserAsync();
        await database.BefriendAsync(first, recipient);
        await database.BefriendAsync(second, recipient);
        SentLetter one = await database.SendAsync(first, recipient, "First sender, first letter");
        SentLetter two = await database.SendAsync(second, recipient, "Second sender");
        SentLetter three = await database.SendAsync(first, recipient, "First sender, second letter");
        using IServiceScope scope = database.Factory.Services.CreateScope();
        ServerDbContext context = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        DateTimeOffset start = DateTimeOffset.UtcNow.AddHours(-1);
        await context.Letters.Where(letter => letter.Id == one.LetterId).ExecuteUpdateAsync(update => update.SetProperty(letter => letter.SentAtUtc, start));
        await context.Letters.Where(letter => letter.Id == two.LetterId).ExecuteUpdateAsync(update => update.SetProperty(letter => letter.SentAtUtc, start.AddMinutes(1)));
        await context.Letters.Where(letter => letter.Id == three.LetterId).ExecuteUpdateAsync(update => update.SetProperty(letter => letter.SentAtUtc, start.AddMinutes(2)));
        MailboxVisit visit = (await recipient.Client.GetFromJsonAsync<MailboxVisit>("/api/v1/mailbox/next"))!;
        Assert.Equal(first.Id, visit.Sender.UserId);
        Assert.Equal(new[] { one.LetterId, three.LetterId }, visit.Letters.Select(letter => letter.Id));
        Assert.Equal("First sender, first letter", visit.Letters[0].Body);
        SentLetter later = await database.SendAsync(first, recipient, "Arrived after the snapshot");
        using HttpResponseMessage read = await recipient.Client.PostAsJsonAsync("/api/v1/letters/read", new
        {
            letterIds = visit.Letters.Select(letter => letter.Id).ToArray()
        });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        MailboxVisit next = (await recipient.Client.GetFromJsonAsync<MailboxVisit>("/api/v1/mailbox/next"))!;
        Assert.Equal(second.Id, next.Sender.UserId);
        Assert.Equal(two.LetterId, Assert.Single(next.Letters).Id);
        using HttpResponseMessage readSecond = await recipient.Client.PostAsync($"/api/v1/letters/{two.LetterId}/read", null);
        Assert.Equal(HttpStatusCode.OK, readSecond.StatusCode);
        MailboxVisit last = (await recipient.Client.GetFromJsonAsync<MailboxVisit>("/api/v1/mailbox/next"))!;
        Assert.Equal(later.LetterId, Assert.Single(last.Letters).Id);
        Assert.Null((await context.Letters.SingleAsync(letter => letter.Id == later.LetterId)).ReadAtUtc);
    }

    [PostgreSqlFact]
    public async Task TiedTimesUseIdOrderingAndQueueSurvivesHostRestart()
    {
        using TestUser first = await database.CreateUserAsync();
        using TestUser second = await database.CreateUserAsync();
        using TestUser recipient = await database.CreateUserAsync();
        await database.BefriendAsync(first, recipient);
        await database.BefriendAsync(second, recipient);
        SentLetter left = await database.SendAsync(first, recipient, "Left");
        SentLetter right = await database.SendAsync(second, recipient, "Right");
        using IServiceScope scope = database.Factory.Services.CreateScope();
        ServerDbContext context = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        DateTimeOffset same = DateTimeOffset.UtcNow.AddHours(-1);
        await context.Letters.Where(letter => letter.RecipientId == recipient.Id).ExecuteUpdateAsync(update => update.SetProperty(letter => letter.SentAtUtc, same));
        Guid expected = left.LetterId.CompareTo(right.LetterId) < 0 ? first.Id : second.Id;
        using HttpResponseMessage response = await recipient.Client.GetAsync("/api/v1/mailbox/next");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        MailboxVisit visit = (await response.Content.ReadFromJsonAsync<MailboxVisit>())!;
        Assert.Equal(expected, visit.Sender.UserId);
        using WebApplicationFactory<Program> restarted = database.Factory.WithWebHostBuilder(_ => { });
        using HttpClient restartedClient = restarted.CreateClient();
        restartedClient.DefaultRequestHeaders.Authorization = recipient.Client.DefaultRequestHeaders.Authorization;
        MailboxVisit restored = (await restartedClient.GetFromJsonAsync<MailboxVisit>("/api/v1/mailbox/next"))!;
        Assert.Equal(visit.Sender.UserId, restored.Sender.UserId);
        Assert.Equal(visit.Letters.Select(letter => letter.Id), restored.Letters.Select(letter => letter.Id));
        Assert.Equal(2, await context.Letters.CountAsync(letter => letter.RecipientId == recipient.Id && letter.ReadAtUtc == null));
    }

    [PostgreSqlFact]
    public async Task RemovedAndBlockedSendersStayHiddenUntilNewConfirmedFriendship()
    {
        using TestUser sender = await database.CreateUserAsync();
        using TestUser recipient = await database.CreateUserAsync();
        using TestUser unrelated = await database.CreateUserAsync();
        await database.BefriendAsync(sender, recipient);
        SentLetter sent = await database.SendAsync(sender, recipient, "Retained while friendship is unavailable");
        for (int index = 0; index < 9; index++)
        {
            await database.SendAsync(sender, recipient, $"Retained letter {index}");
        }
        using HttpResponseMessage removed = await sender.Client.DeleteAsync($"/api/v1/friends/{recipient.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using HttpResponseMessage hidden = await recipient.Client.GetAsync("/api/v1/mailbox/next");
        Assert.Equal(HttpStatusCode.NoContent, hidden.StatusCode);
        using HttpResponseMessage blocked = await recipient.Client.PostAsync($"/api/v1/users/{sender.Id}/block", null);
        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);
        using HttpResponseMessage unblocked = await recipient.Client.DeleteAsync($"/api/v1/users/{sender.Id}/block");
        Assert.Equal(HttpStatusCode.NoContent, unblocked.StatusCode);
        using HttpResponseMessage stillHidden = await recipient.Client.GetAsync("/api/v1/mailbox/next");
        Assert.Equal(HttpStatusCode.NoContent, stillHidden.StatusCode);
        await database.BefriendAsync(sender, recipient);
        MailboxVisit returned = (await recipient.Client.GetFromJsonAsync<MailboxVisit>("/api/v1/mailbox/next"))!;
        Assert.Equal(10, returned.Letters.Count);
        Assert.Contains(returned.Letters, letter => letter.Id == sent.LetterId);
        using HttpResponseMessage otherMailbox = await unrelated.Client.GetAsync($"/api/v1/mailbox/next?userId={recipient.Id}");
        Assert.Equal(HttpStatusCode.NoContent, otherMailbox.StatusCode);
        using HttpClient anonymous = database.Factory.CreateClient();
        using HttpResponseMessage forbidden = await anonymous.GetAsync("/api/v1/mailbox/next");
        Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
    }
}
