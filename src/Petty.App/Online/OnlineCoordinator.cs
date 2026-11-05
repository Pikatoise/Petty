using System.Collections.Concurrent;
using System.Threading.Channels;
using Petty.Online.Client;

namespace Petty.App.Online;

/// <summary>Optional network lifecycle. Never participates in local simulation, saves or shutdown decisions.</summary>
internal sealed class OnlineCoordinator : IAsyncDisposable
{
    private readonly OnlineClient _client;
    private readonly Action<Action> _dispatch;
    private readonly Action _unavailable;
    private readonly Channel<bool> _wakeups = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private Task? _fallback;
    private PetProfile? _pet;
    private readonly ConcurrentQueue<Guid> _revoked = new();
    private bool _disposed;

    public OnlineCoordinator(OnlineClient client, Action<Action> dispatch, Action unavailable)
    {
        _client = client;
        _dispatch = dispatch;
        _unavailable = unavailable;
        _client.SessionChanged += Wake;
    }

    public OnlineClient Client => _client;
    public Guid? PublishedSessionId
    {
        get; private set;
    }
    public AccountProfile? Account
    {
        get; private set;
    }
    public IReadOnlyList<Friend> Friends { get; private set; } = [];
    public MailboxVisit? Mailbox
    {
        get; private set;
    }
    public event Action? AccountChanged;
    public event Action? MailboxChanged;

    public static OnlineCoordinator? FromEnvironment(string dataDirectory, Action<Action> dispatch, Action unavailable)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        string? origin = Environment.GetEnvironmentVariable("PETTY_SERVER_URL");
        if (string.IsNullOrWhiteSpace(origin))
        {
            return null;
        }
        Uri server = OnlineClient.ValidateServer(new Uri(origin, UriKind.Absolute));
        return new OnlineCoordinator(new OnlineClient(server, new ProtectedTokenStore(dataDirectory, server)), dispatch, unavailable);
    }

    public Task StartAsync()
    {
        _worker ??= Task.Run(() => RunAsync(_stop.Token));
        _fallback ??= Task.Run(() => FallbackAsync(_stop.Token));
        return Task.CompletedTask;
    }

    public void UpdatePet(PetProfile profile)
    {
        Volatile.Write(ref _pet, profile);
        Wake();
    }
    // D2 calls only after the previous visitor has physically exited.
    public void VisitCompleted() => Wake();
    public void Refresh() => Wake();
    private void Wake() => _wakeups.Writer.TryWrite(true);

    private async Task RunAsync(CancellationToken ct)
    {
        MailConnection? connection = null;
        CancellationTokenSource? connectionStop = null;
        Task? connectionTask = null;
        Guid? connectedSession = null;
        PetProfile? syncedPet = null;
        int failures = 0;
        try
        {
            try
            {
                await _client.RestoreAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested) { NotifyUnavailable(); }
            Wake();
            while (await _wakeups.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_wakeups.Reader.TryRead(out _))
                {
                }
                await Task.Delay(150, ct).ConfigureAwait(false);
                try
                {
                    while (_revoked.TryDequeue(out Guid revoked))
                    {
                        await _client.InvalidateAsync(revoked).ConfigureAwait(false);
                    }
                    Guid? session = _client.SessionId;
                    if (session != connectedSession)
                    {
                        if (connectionStop is not null)
                        {
                            await connectionStop.CancelAsync().ConfigureAwait(false);
                        }
                        if (connectionTask is not null)
                        {
                            await IgnoreCancellationAsync(connectionTask).ConfigureAwait(false);
                        }
                        if (connection is not null)
                        {
                            await connection.DisposeAsync().ConfigureAwait(false);
                        }
                        connectionStop?.Dispose();
                        connection = null;
                        connectionTask = null;
                        connectedSession = session;
                        syncedPet = null;
                        if (session is { } id)
                        {
                            connectionStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            connection = new MailConnection(_client, Wake, () => { _revoked.Enqueue(id); Wake(); });
                            connectionTask = connection.RunAsync(Wake, connectionStop.Token);
                        }
                    }
                    if (session is null)
                    {
                        Publish(null, [], null, null);
                        continue;
                    }
                    AccountProfile account = await _client.GetMeAsync(ct).ConfigureAwait(false);
                    PetProfile? pet = Volatile.Read(ref _pet);
                    if (pet is not null && pet != syncedPet)
                    {
                        await _client.UpdatePetAsync(pet, ct).ConfigureAwait(false);
                        syncedPet = pet;
                        account = await _client.GetMeAsync(ct).ConfigureAwait(false);
                    }
                    Friend[] friends = await _client.GetFriendsAsync(ct).ConfigureAwait(false);
                    MailboxVisit? mailbox = await _client.GetNextVisitAsync(ct).ConfigureAwait(false);
                    Publish(account, friends, mailbox, session);
                    failures = 0;
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    if (_client.SessionId is null)
                    {
                        Publish(null, [], null, null);
                    }
                    NotifyUnavailable();
                    if (_client.SessionId is not null)
                    {
                        double seconds = Math.Min(60, Math.Pow(2, Math.Min(failures++, 6)));
                        await Task.Delay(TimeSpan.FromSeconds(seconds + Random.Shared.NextDouble()), ct).ConfigureAwait(false);
                        Wake();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            if (connectionStop is not null)
            {
                await connectionStop.CancelAsync().ConfigureAwait(false);
            }
            if (connectionTask is not null)
            {
                await IgnoreCancellationAsync(connectionTask).ConfigureAwait(false);
            }
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            connectionStop?.Dispose();
        }
    }

    private void Publish(AccountProfile? account, IReadOnlyList<Friend> friends, MailboxVisit? mailbox, Guid? session)
    {
        _dispatch(() =>
        {
            if (_stop.IsCancellationRequested || session != _client.SessionId)
            {
                return;
            }
            Account = account;
            PublishedSessionId = session;
            Friends = friends;
            Mailbox = mailbox;
            AccountChanged?.Invoke();
            MailboxChanged?.Invoke();
        });
    }

    private void NotifyUnavailable() => _dispatch(() => { if (!_stop.IsCancellationRequested) { _unavailable(); } });

    private async Task FallbackAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(10 + Random.Shared.NextDouble() * 5), ct).ConfigureAwait(false);
                Wake();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    public async Task StopAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_worker is not null)
        {
            await _worker.ConfigureAwait(false);
        }
        if (_fallback is not null)
        {
            await _fallback.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _client.SessionChanged -= Wake;
        await StopAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
    }
}
