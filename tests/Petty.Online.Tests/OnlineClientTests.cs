using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Petty.App.Online;
using Petty.Online.Client;

namespace Petty.Online.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "DPAPI verification requires Windows.";
        }
    }
}

public sealed class OnlineClientTests
{
    internal sealed class MemoryStore : ITokenStore
    {
        public SessionTokens? Tokens
        {
            get; set;
        }
        public int Saves
        {
            get; private set;
        }
        public Task<SessionTokens?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Tokens);
        public Task SaveAsync(SessionTokens tokens, CancellationToken cancellationToken)
        {
            Tokens = tokens;
            Saves++;
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken)
        {
            Tokens = null;
            return Task.CompletedTask;
        }
    }

    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    internal static SessionTokens Tokens(string access = "access", string refresh = "refresh", bool expired = false) => new()
    {
        SessionId = Guid.NewGuid(),
        AccessToken = access,
        RefreshToken = refresh,
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(expired ? -1 : 15)
    };

    internal static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };

    [Fact]
    public async Task ParallelCallsRotateOnceAndPersistBeforeUsingRefreshedCredentials()
    {
        var store = new MemoryStore { Tokens = Tokens(expired: true) };
        int refreshes = 0;
        var handler = new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/refresh"))
            {
                Interlocked.Increment(ref refreshes);
                JsonElement body = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct));
                Assert.Equal("refresh", body.GetProperty("refreshToken").GetString());
                return Json(Tokens("renewed", "rotated"));
            }
            Assert.Equal("renewed", request.Headers.Authorization!.Parameter);
            Assert.Equal("rotated", store.Tokens!.RefreshToken);
            return Json(new AccountProfile(Guid.NewGuid(), "login", null, null, null));
        });
        await using var client = new OnlineClient(new Uri("https://example.test"), store, handler);
        await client.RestoreAsync();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetMeAsync()));
        Assert.Equal(1, refreshes);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task LostSendResponseRetriesSamePayloadAndQuotaErrorRemainsStructured()
    {
        var store = new MemoryStore { Tokens = Tokens() };
        var letter = new SendLetter(Guid.NewGuid(), "Message 🐈", Guid.NewGuid());
        int calls = 0;
        string? first = null;
        var handler = new Handler(async (request, ct) =>
        {
            string body = await request.Content!.ReadAsStringAsync(ct);
            if (++calls == 1)
            {
                first = body;
                throw new HttpRequestException("Lost response");
            }
            Assert.Equal(first, body);
            return Json(new
            {
                code = "UNREAD_MAIL_LIMIT_REACHED",
                unreadCount = 10,
                limit = 10
            }, HttpStatusCode.Conflict);
        });
        await using var client = new OnlineClient(new Uri("https://example.test"), store, handler);
        await client.RestoreAsync();
        OnlineException error = await Assert.ThrowsAsync<OnlineException>(() => client.SendLetterAsync(letter));
        Assert.Equal("UNREAD_MAIL_LIMIT_REACHED", error.Code);
        Assert.Equal(10, error.UnreadCount);
        Assert.Equal(10, error.Limit);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RevocationClearsStoredSessionWithoutRefreshingAndOldHintCannotClearNewLogin()
    {
        var store = new MemoryStore { Tokens = Tokens() };
        Guid old = store.Tokens.SessionId;
        SessionTokens next = Tokens("next", "next-refresh");
        int refreshes = 0;
        var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/login"))
            {
                return Task.FromResult(Json(next));
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/refresh"))
            {
                refreshes++;
            }
            return Task.FromResult(Json(new
            {
                code = "SESSION_REVOKED"
            }, HttpStatusCode.Unauthorized));
        });
        await using var client = new OnlineClient(new Uri("https://example.test"), store, handler);
        await client.RestoreAsync();
        await Assert.ThrowsAsync<OnlineException>(() => client.GetMeAsync());
        Assert.Null(client.SessionId);
        Assert.Null(store.Tokens);
        Assert.Equal(0, refreshes);
        await client.LoginAsync("login", "unsaved-password");
        await client.InvalidateAsync(old);
        Assert.Equal(next.SessionId, client.SessionId);
        Assert.DoesNotContain("next-refresh", next.ToString());
    }

    [Fact]
    public async Task UnsafeMutationIsNotRetriedAndLogoutClearsLocalSessionEvenOffline()
    {
        var store = new MemoryStore { Tokens = Tokens() };
        int calls = 0;
        var handler = new Handler((_, _) => { calls++; throw new HttpRequestException("Offline"); });
        await using var client = new OnlineClient(new Uri("https://example.test"), store, handler);
        await client.RestoreAsync();
        await Assert.ThrowsAsync<HttpRequestException>(() => client.RequestFriendAsync("login"));
        Assert.Equal(1, calls);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.LogoutAsync());
        Assert.Null(client.SessionId);
        Assert.Null(store.Tokens);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CancellationStopsRequestAndDisposalWithoutTouchingLocalFiles()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new MemoryStore { Tokens = Tokens() };
        var handler = new Handler(async (_, ct) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); });
        await using var client = new OnlineClient(new Uri("https://example.test"), store, handler);
        await client.RestoreAsync();
        using var cancellation = new CancellationTokenSource();
        Task request = client.GetMeAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.NotNull(store.Tokens);
    }

    [Theory]
    [InlineData("http://example.test")]
    [InlineData("https://login:password@example.test")]
    [InlineData("https://example.test/path")]
    [InlineData("https://example.test/?token=secret")]
    public void RefusesUnsafeServerOrigins(string origin) => Assert.Throws<ArgumentException>(() => OnlineClient.ValidateServer(new Uri(origin)));

    [Fact]
    public async Task CoordinatorStopsPendingNetworkAndSuppressesQueuedDispatcherUpdates()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var store = new MemoryStore { Tokens = Tokens() };
        var handler = new Handler(async (_, ct) => { pending.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); });
        var client = new OnlineClient(new Uri("http://127.0.0.1:1"), store, handler);
        var coordinator = new OnlineCoordinator(client, callbacks.Enqueue, () => throw new InvalidOperationException("Must not publish after shutdown"));
        await coordinator.StartAsync();
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        while (callbacks.TryDequeue(out Action? callback))
        {
            callback();
        }
        Assert.Null(coordinator.Account);
        Assert.NotNull(store.Tokens);
        await coordinator.DisposeAsync();
    }

    [WindowsFact]
    public async Task WindowsStoreEncryptsRotatesAndDiscardsCorruptionInIsolatedDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        string directory = Path.Combine(Path.GetTempPath(), "petty-online-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ProtectedTokenStore(directory, new Uri("https://example.test"));
            SessionTokens original = Tokens("unique-private-access", "unique-private-refresh");
            await store.SaveAsync(original, default);
            string file = Assert.Single(Directory.GetFiles(directory));
            string raw = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
            Assert.DoesNotContain(original.RefreshToken, raw);
            Assert.DoesNotContain(original.AccessToken, raw);
            Assert.Equal(original.RefreshToken, (await store.LoadAsync(default))!.RefreshToken);
            SessionTokens rotated = Tokens("rotated-access", "rotated-refresh");
            await store.SaveAsync(rotated, default);
            Assert.Single(Directory.GetFiles(directory));
            Assert.Equal(rotated.RefreshToken, (await store.LoadAsync(default))!.RefreshToken);
            await File.WriteAllBytesAsync(file, [1, 2, 3]);
            Assert.Null(await store.LoadAsync(default));
            Assert.Empty(Directory.GetFiles(directory));
            await store.ClearAsync(default);
        }
        finally { Directory.Delete(directory); }
    }
}
