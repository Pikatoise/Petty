using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Petty.App.Online;
using Petty.Online.Client;

namespace Petty.Online.Tests;

public sealed class OnlineCoordinatorTests
{
    [Fact]
    public async Task MailboxWakeupsFetchOnlyMailAndRetainAnInvalidationDuringAnInFlightRequest()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var requests = new ConcurrentDictionary<string, int>();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new SemaphoreSlim(0);
        var store = new OnlineClientTests.MemoryStore { Tokens = OnlineClientTests.Tokens() };
        var handler = new OnlineClientTests.Handler(async (request, ct) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            int count = requests.AddOrUpdate(path, 1, (_, previous) => previous + 1);
            if (path == "/api/v1/mailbox/next")
            {
                if (count == 2)
                {
                    held.TrySetResult();
                    await release.Task.WaitAsync(ct);
                }
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return path switch
            {
                "/api/v1/me" => OnlineClientTests.Json(new AccountProfile(Guid.NewGuid(), "owner", null, null, null)),
                "/api/v1/friends" => OnlineClientTests.Json(Array.Empty<Friend>()),
                "/api/v1/friends/requests" => OnlineClientTests.Json(new FriendRequests([], [])),
                _ => throw new InvalidOperationException(path)
            };
        });
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var client = new OnlineClient(new Uri($"http://127.0.0.1:{port}"), store, handler);
        await using var online = new OnlineCoordinator(client, action => action(), () => { });
        online.MailboxChanged += () => published.Release();
        await online.StartAsync();
        Assert.True(await published.WaitAsync(TimeSpan.FromSeconds(5)));
        online.VisitCompleted();
        await held.Task.WaitAsync(TimeSpan.FromSeconds(5));
        long fresh = online.VisitCompleted();
        release.TrySetResult();
        Assert.True(await published.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await published.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(fresh, online.MailboxVersion);
        Assert.Equal(3, requests["/api/v1/mailbox/next"]);
        Assert.Equal(1, requests["/api/v1/me"]);
        Assert.Equal(1, requests["/api/v1/friends"]);
        Assert.Equal(1, requests["/api/v1/friends/requests"]);
    }

    [Fact]
    public async Task UnchangedSnapshotsDoNotInvalidateFriendsAndMailboxFreshnessIsPreserved()
    {
        var store = new OnlineClientTests.MemoryStore { Tokens = OnlineClientTests.Tokens() };
        var client = new OnlineClient(new Uri("https://example.test"), store);
        await client.RestoreAsync();
        await using var online = new OnlineCoordinator(client, action => action(), () => { });
        var account = new AccountProfile(Guid.NewGuid(), "owner", "Milo", "british-shorthair-blue", null);
        var friend = new Friend(Guid.NewGuid(), "friend", "Bob", "british-shorthair-blue");
        int accounts = 0, friendships = 0, mailboxes = 0;
        online.AccountChanged += () => accounts++;
        online.FriendsChanged += () => friendships++;
        online.MailboxChanged += () => mailboxes++;
        online.Publish(account, [friend], new([], []), null, client.SessionId, true, 1);
        online.Publish(account, [friend], new([], []), null, client.SessionId, true, 2);
        Assert.Equal(1, accounts);
        Assert.Equal(1, friendships);
        Assert.Equal(2, mailboxes);
        Assert.Equal(2, online.MailboxVersion);

        var request = new FriendRequest(Guid.NewGuid(), friend, DateTimeOffset.UtcNow);
        online.Publish(account, [friend], new([request], []), null, client.SessionId, false);
        Assert.Equal(2, friendships);
        Assert.Equal(2, online.MailboxVersion);
        Assert.Single(online.Requests.Incoming);

        online.Publish(account with
        {
            Login = "stale"
        }, [], new([], []), null, Guid.NewGuid(), true, 3);
        Assert.Equal("owner", online.Account!.Login);
        Assert.Single(online.Friends);
        Assert.Equal(2, online.MailboxVersion);
    }

    [Fact]
    public async Task CompletionRequiresAMailboxRequestNewerThanAnInFlightSnapshot()
    {
        var store = new OnlineClientTests.MemoryStore { Tokens = OnlineClientTests.Tokens() };
        var client = new OnlineClient(new Uri("https://example.test"), store);
        await client.RestoreAsync();
        await using var online = new OnlineCoordinator(client, action => action(), () => { });
        long inFlight = online.VisitCompleted();
        long afterExit = online.VisitCompleted();
        online.Publish(null, [], new([], []), null, client.SessionId, true, inFlight);
        Assert.True(online.MailboxVersion < afterExit);
        online.Publish(null, [], new([], []), null, client.SessionId, true, afterExit);
        Assert.Equal(afterExit, online.MailboxVersion);
    }
}
