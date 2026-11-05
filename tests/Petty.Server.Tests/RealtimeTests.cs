using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Petty.Server.Api.Realtime;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Application.Features.Delivery;
using Petty.Server.Application.Features.Letters;
using Petty.Server.Application.Notifications;

namespace Petty.Server.Tests;

[Collection("PostgreSQL")]
public sealed class RealtimeTests(PostgreSqlDatabase database)
{
    [PostgreSqlFact]
    public async Task WebSocketsReceivePayloadFreeWakeupsOnlyInCurrentSession()
    {
        using TestUser sender = await database.CreateUserAsync();
        using TestUser recipient = await database.CreateUserAsync();
        using TestUser unrelated = await database.CreateUserAsync();
        await database.BefriendAsync(sender, recipient);
        string oldToken = recipient.Client.DefaultRequestHeaders.Authorization!.Parameter!;
        await using Probe oldConnection = await Probe.Connect(database.Factory, oldToken);
        await using Probe senderConnection = await Probe.Connect(database.Factory, sender.Client.DefaultRequestHeaders.Authorization!.Parameter!);
        await using Probe unrelatedConnection = await Probe.Connect(database.Factory, unrelated.Client.DefaultRequestHeaders.Authorization!.Parameter!);

        Task<SentLetter> sending = database.SendAsync(sender, recipient, "Private body must never enter a SignalR frame");
        Assert.Equal(0, (await oldConnection.WaitFor(MailSignals.MailboxChanged)).ArgumentCount);
        MailboxVisit visibleAfterSignal = (await recipient.Client.GetFromJsonAsync<MailboxVisit>("/api/v1/mailbox/next"))!;
        SentLetter first = await sending;
        Assert.Equal(first.LetterId, Assert.Single(visibleAfterSignal.Letters).Id);
        Assert.Empty(await senderConnection.Barrier(database.Factory.Services));
        Assert.Empty(await unrelatedConnection.Barrier(database.Factory.Services));

        TokenResponse renewed = await database.LoginAsync(recipient);
        Assert.Equal(0, (await oldConnection.WaitFor(MailSignals.SessionRevoked)).ArgumentCount);
        using HttpClient revoked = database.Factory.CreateClient();
        revoked.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);
        using HttpResponseMessage denied = await revoked.PostAsync("/hubs/mail/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await using Probe current = await Probe.Connect(database.Factory, renewed.AccessToken);
        await database.SendAsync(sender, recipient, "New session's letter");
        Assert.Equal(0, (await current.WaitFor(MailSignals.MailboxChanged)).ArgumentCount);
        // A same-connection barrier observes all earlier queued frames without timing-based negative assertions.
        Assert.Empty(await oldConnection.Barrier(database.Factory.Services));
        Assert.Empty(await unrelatedConnection.Barrier(database.Factory.Services));
        using HttpResponseMessage removed = await sender.Client.DeleteAsync($"/api/v1/friends/{recipient.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(0, (await current.WaitFor(MailSignals.FriendshipsChanged)).ArgumentCount);
        Assert.Equal(0, (await senderConnection.WaitFor(MailSignals.FriendshipsChanged)).ArgumentCount);
        using HttpResponseMessage hidden = await recipient.Client.GetAsync("/api/v1/mailbox/next");
        Assert.Equal(HttpStatusCode.NoContent, hidden.StatusCode);
        await database.BefriendAsync(sender, recipient);
        Assert.Equal(0, (await current.WaitFor(MailSignals.MailboxChanged)).ArgumentCount);
        MailboxVisit restored = (await recipient.Client.GetFromJsonAsync<MailboxVisit>("/api/v1/mailbox/next"))!;
        Assert.Equal(2, restored.Letters.Count);
    }

    [PostgreSqlFact]
    public async Task AuthenticationExpiryClosesWebSocketAndCachedRevocationFailsHubFilter()
    {
        using TestUser user = await database.CreateUserAsync();
        string oldToken = user.Client.DefaultRequestHeaders.Authorization!.Parameter!;
        Guid oldSession = SessionId(oldToken);
        await database.LoginAsync(user);
        using IServiceScope scope = database.Factory.Services.CreateScope();
        var caller = new CachedCaller(user.Id, oldSession);
        var invocation = new HubInvocationContext(caller, scope.ServiceProvider, new MailHub(),
            typeof(Hub).GetMethod(nameof(Hub.OnConnectedAsync))!, []);
        bool called = false;
        await Assert.ThrowsAsync<HubException>(async () => await new SessionHubFilter().InvokeMethodAsync(invocation, _ =>
        {
            called = true;
            return ValueTask.FromResult<object?>(null);
        }));
        Assert.True(caller.Aborted);
        Assert.False(called);

        IAccessTokenIssuer issuer = scope.ServiceProvider.GetRequiredService<IAccessTokenIssuer>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string shortLived = issuer.Issue(user.Id, SessionId(user.Client.DefaultRequestHeaders.Authorization!.Parameter!), now.AddSeconds(-1), now.AddSeconds(3));
        await using Probe connection = await Probe.Connect(database.Factory, shortLived);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Connection.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(HubConnectionState.Disconnected, connection.Connection.State);
    }

    [PostgreSqlFact]
    public async Task FailedPushCannotRollBackCommittedLetterOrTurnSuccessIntoError()
    {
        using TestUser sender = await database.CreateUserAsync();
        using TestUser recipient = await database.CreateUserAsync();
        await database.BefriendAsync(sender, recipient);
        using WebApplicationFactory<Program> failing = database.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHubContext<MailHub>>();
            services.AddSingleton<IHubContext<MailHub>, UnavailableHub>();
        }));
        using HttpClient client = failing.CreateClient();
        client.DefaultRequestHeaders.Authorization = sender.Client.DefaultRequestHeaders.Authorization;
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/letters",
            new
            {
                recipientUserId = recipient.Id,
                body = "Committed despite a failed wakeup",
                clientRequestId = Guid.NewGuid()
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        SentLetter letter = (await response.Content.ReadFromJsonAsync<SentLetter>())!;
        MailboxVisit visit = (await recipient.Client.GetFromJsonAsync<MailboxVisit>("/api/v1/mailbox/next"))!;
        Assert.Equal(letter.LetterId, Assert.Single(visit.Letters).Id);
    }

    private static Guid SessionId(string token) => Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Single(claim => claim.Type == "sid").Value);

    private sealed record Signal(string Name, int ArgumentCount);

    private sealed class Probe(HubConnection connection, Guid sessionId, Channel<Signal> signals) : IAsyncDisposable
    {
        public HubConnection Connection => connection;

        public static async Task<Probe> Connect(WebApplicationFactory<Program> factory, string token)
        {
            var signals = Channel.CreateUnbounded<Signal>();
            HubConnection connection = new HubConnectionBuilder().WithUrl("http://localhost/hubs/mail", options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    WebSocketClient client = factory.Server.CreateWebSocketClient();
                    client.ConfigureRequest = request => request.Headers.Authorization = "Bearer " + token;
                    return await client.ConnectAsync(context.Uri, cancellationToken);
                };
            }).Build();
            foreach (string name in new[] { MailSignals.MailboxChanged, MailSignals.FriendshipsChanged, MailSignals.SessionRevoked, "TestBarrier" })
            {
                connection.On(name, [], (arguments, _) =>
                {
                    signals.Writer.TryWrite(new Signal(name, arguments.Length));
                    return Task.CompletedTask;
                }, signals);
            }
            await connection.StartAsync().WaitAsync(TimeSpan.FromSeconds(8));
            // An unsupported invocation's completion also confirms the server has registered this connection.
            await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("UnsupportedReadyProbe").WaitAsync(TimeSpan.FromSeconds(8)));
            return new Probe(connection, SessionId(token), signals);
        }

        public async Task<Signal> WaitFor(string name)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (true)
            {
                Signal signal = await signals.Reader.ReadAsync(deadline.Token);
                Assert.Equal(0, signal.ArgumentCount);
                if (signal.Name == name)
                {
                    return signal;
                }
            }
        }

        public async Task<IReadOnlyList<Signal>> Barrier(IServiceProvider services)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await services.GetRequiredService<IHubContext<MailHub>>().Clients.User(sessionId.ToString("N"))
                .SendCoreAsync("TestBarrier", [], deadline.Token);
            var prior = new List<Signal>();
            while (true)
            {
                Signal signal = await signals.Reader.ReadAsync(deadline.Token);
                Assert.Equal(0, signal.ArgumentCount);
                if (signal.Name == "TestBarrier")
                {
                    return prior;
                }
                prior.Add(signal);
            }
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class CachedCaller(Guid userId, Guid sessionId) : HubCallerContext
    {
        public bool Aborted
        {
            get; private set;
        }
        public override string ConnectionId => "cached-test-connection";
        public override string? UserIdentifier => sessionId.ToString("N");
        public override ClaimsPrincipal User
        {
            get;
        } = new(new ClaimsIdentity([
            new Claim("sub", userId.ToString()), new Claim("sid", sessionId.ToString())], "Bearer"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => Aborted = true;
    }

    private sealed class UnavailableHub : IHubContext<MailHub>
    {
        public IHubClients Clients => throw new InvalidOperationException("Synthetic unavailable push transport");
        public IGroupManager Groups => throw new InvalidOperationException("Synthetic unavailable push transport");
    }
}
