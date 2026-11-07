using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Petty.Online.Client;

namespace Petty.Server.Tests;

[Collection("PostgreSQL")]
public sealed class OnlineClientIntegrationTests(PostgreSqlDatabase database)
{
    private sealed class MemoryStore : ITokenStore
    {
        public SessionTokens? Tokens
        {
            get; set;
        }
        public Task<SessionTokens?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Tokens);
        public Task SaveAsync(SessionTokens tokens, CancellationToken cancellationToken)
        {
            Tokens = tokens;
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken)
        {
            Tokens = null;
            return Task.CompletedTask;
        }
    }

    [PostgreSqlFact]
    public async Task TypedClientRegistersRefreshesSendsReadsAndLogsOutAgainstPostgreSql()
    {
        var firstStore = new MemoryStore();
        await using var first = new OnlineClient(new Uri("http://localhost"), firstStore, database.Factory.Server.CreateHandler());
        await using var second = new OnlineClient(new Uri("http://localhost"), new MemoryStore(), database.Factory.Server.CreateHandler());
        string login = "client_" + Guid.NewGuid().ToString("N")[..20];
        string otherLogin = "client_" + Guid.NewGuid().ToString("N")[..20];
        const string password = "ClientPassword12345";
        await first.RegisterAsync(login, password, password);
        await second.RegisterAsync(otherLogin, password, password);
        await first.LoginAsync(login, password);
        await second.LoginAsync(otherLogin, password);
        AccountProfile other = await second.GetMeAsync();
        await first.UpdatePetAsync(new PetProfile("Kitty", "british-shorthair-blue"));
        Assert.Equal("Kitty", (await first.GetMeAsync()).PetName);
        Guid request = await first.RequestFriendAsync(otherLogin);
        Assert.Equal(request, Assert.Single((await second.GetFriendRequestsAsync()).Incoming).RequestId);
        await second.RespondToFriendRequestAsync(request, true);
        Assert.Equal(other.UserId, Assert.Single(await first.GetFriendsAsync()).UserId);

        SessionTokens current = firstStore.Tokens!;
        firstStore.Tokens = new SessionTokens
        {
            AccessToken = current.AccessToken,
            RefreshToken = current.RefreshToken,
            SessionId = current.SessionId,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        await first.RestoreAsync();
        await Task.WhenAll(first.GetMeAsync(), first.GetFriendsAsync());
        Assert.NotEqual(current.RefreshToken, firstStore.Tokens!.RefreshToken);

        var send = new SendLetter(other.UserId, "Typed client 🐕", Guid.NewGuid());
        SentLetter letter = await first.SendLetterAsync(send);
        Assert.Equal(letter.LetterId, (await first.SendLetterAsync(send)).LetterId);
        Assert.Equal(1, (await first.GetQuotaAsync(other.UserId)).UnreadCount);
        Assert.Empty((await second.GetHistoryAsync(true)).Items);
        Assert.Equal(letter.LetterId, Assert.Single((await second.GetNextVisitAsync())!.Letters).Id);
        ReadLettersResult read = await second.ReadLettersAsync([letter.LetterId]);
        Assert.Equal(letter.LetterId, Assert.Single(read.Letters).LetterId);
        Assert.Null(await second.GetNextVisitAsync());
        Assert.Equal(send.Body, Assert.Single((await second.GetHistoryAsync(true)).Items).Body);
        Assert.Single((await first.GetHistoryAsync(false)).Items);
        await first.LogoutAsync();
        Assert.Null(firstStore.Tokens);
        Assert.Equal("LOGIN_REQUIRED", (await Assert.ThrowsAsync<OnlineException>(() => first.GetMeAsync())).Code);
    }

    [PostgreSqlFact]
    public async Task ClientReconnectsAfterTransportLossAndHandlesRevocationSignal()
    {
        using TestUser user = await database.CreateUserAsync();
        var store = new MemoryStore();
        await using var client = new OnlineClient(new Uri("http://localhost"), store, database.Factory.Server.CreateHandler());
        await client.LoginAsync(user.Login, "TestPassword12345");
        var connected = Channel.CreateUnbounded<bool>();
        var revoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WebSocket? socket = null;
        int attempts = 0;
        await using var mail = new MailConnection(client, () => { }, () => revoked.TrySetResult(), options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.SkipNegotiation = true;
            options.WebSocketFactory = async (context, ct) =>
            {
                // Initial failure must retry too, not only an established connection's disconnect.
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new HttpRequestException("Synthetic initial transport loss");
                }
                WebSocketClient transport = database.Factory.Server.CreateWebSocketClient();
                transport.ConfigureRequest = request => request.Headers.Authorization = "Bearer " + store.Tokens!.AccessToken;
                socket = await transport.ConnectAsync(context.Uri, ct);
                return socket;
            };
        });
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task run = mail.RunAsync(() => connected.Writer.TryWrite(true), stop.Token);
        try
        {
            await connected.Reader.ReadAsync(stop.Token);
            socket!.Abort();
            await connected.Reader.ReadAsync(stop.Token);
            Assert.True(attempts >= 3);
            await Assert.ThrowsAsync<HubException>(() => mail.Hub.InvokeAsync("UnsupportedReadyProbe", stop.Token));
            await database.LoginAsync(user);
            await revoked.Task.WaitAsync(stop.Token);
            Guid old = client.SessionId!.Value;
            await client.InvalidateAsync(old);
            Assert.Null(store.Tokens);
            Assert.Null(client.SessionId);
        }
        finally
        {
            await stop.CancelAsync();
            try
            {
                await run;
            }
            catch (OperationCanceledException) { }
        }
    }
}
