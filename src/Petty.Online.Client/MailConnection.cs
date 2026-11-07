using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace Petty.Online.Client;

/// <summary>Wakeups only. A single cancellation-owned loop covers initial failure and every disconnect.</summary>
public sealed class MailConnection : IAsyncDisposable
{
    private readonly HubConnection _hub;
    private TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MailConnection(OnlineClient client, Action changed, Action revoked) : this(client, changed, changed, revoked) { }

    public MailConnection(OnlineClient client, Action mailboxChanged, Action friendshipsChanged, Action revoked)
        : this(client, mailboxChanged, friendshipsChanged, revoked, _ => { }) { }

    internal MailConnection(OnlineClient client, Action changed, Action revoked, Action<HttpConnectionOptions> configure)
        : this(client, changed, changed, revoked, configure) { }

    private MailConnection(OnlineClient client, Action mailboxChanged, Action friendshipsChanged, Action revoked, Action<HttpConnectionOptions> configure)
    {
        _hub = new HubConnectionBuilder().WithUrl(new Uri(client.Server, "hubs/mail"), options =>
        {
            options.AccessTokenProvider = () => client.GetAccessTokenAsync(_lifetime);
            options.HttpMessageHandlerFactory = handler =>
            {
                if (handler is HttpClientHandler http)
                {
                    http.AllowAutoRedirect = false;
                }
                return handler;
            };
            configure(options);
        }).Build();
        _hub.On("MailboxChanged", mailboxChanged);
        _hub.On("FriendshipsChanged", friendshipsChanged);
        _hub.On("SessionRevoked", revoked);
        _hub.Closed += _ => { _closed.TrySetResult(); return Task.CompletedTask; };
    }

    private CancellationToken _lifetime;
    internal HubConnection Hub => _hub;

    public async Task RunAsync(Action reconnected, CancellationToken ct)
    {
        _lifetime = ct;
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await _hub.StartAsync(ct).ConfigureAwait(false);
                failures = 0;
                reconnected();
                await _closed.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                // Transport startup can wrap cancellation in an AggregateException.
                break;
            }
            catch (Exception)
            {
                // No exception text: transports can include URLs or authentication information.
            }
            if (ct.IsCancellationRequested)
            {
                break;
            }
            double seconds = Math.Min(60, Math.Pow(2, Math.Min(failures++, 6)));
            await Task.Delay(TimeSpan.FromSeconds(seconds + Random.Shared.NextDouble()), ct).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => _hub.DisposeAsync();
}
