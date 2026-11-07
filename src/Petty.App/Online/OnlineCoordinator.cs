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
    private int _pending;
    private long _requestedMailboxVersion;
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
        _client.SessionChanged += Refresh;
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
    public FriendRequests Requests { get; private set; } = new([], []);
    public IReadOnlyList<BlockedUser> BlockedUsers { get; private set; } = [];
    public long MailboxVersion
    {
        get; private set;
    }
    public event Action? FriendsChanged;
    public event Action<bool>? FriendsRefreshCompleted;
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
        Wake(RefreshKind.Account);
    }
    // D2 calls only after the previous visitor has physically exited.
    public long VisitCompleted() => Wake(RefreshKind.Mailbox);
    public void Refresh() => Wake(RefreshKind.All);
    public void RefreshFriends() => Wake(RefreshKind.Friends | RefreshKind.Mailbox);

    [Flags]
    private enum RefreshKind
    {
        Account = 1, Friends = 2, Mailbox = 4, All = Account | Friends | Mailbox
    }

    private long Wake(RefreshKind kind)
    {
        long version = (kind & RefreshKind.Mailbox) != 0
            ? Interlocked.Increment(ref _requestedMailboxVersion)
            : Volatile.Read(ref _requestedMailboxVersion);
        Interlocked.Or(ref _pending, (int)kind);
        _wakeups.Writer.TryWrite(true);
        return version;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        MailConnection? connection = null;
        CancellationTokenSource? connectionStop = null;
        Task? connectionTask = null;
        Guid? connectedSession = null;
        PetProfile? syncedPet = null;
        int failures = 0;
        AccountProfile? account = null;
        Friend[] friends = [];
        FriendRequests requests = new([], []);
        BlockedUser[] blocked = [];
        MailboxVisit? mailbox = null;
        try
        {
            try
            {
                await _client.RestoreAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested) { NotifyUnavailable(); }
            Refresh();
            while (await _wakeups.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                await Task.Delay(150, ct).ConfigureAwait(false);
                while (_wakeups.Reader.TryRead(out _))
                {
                }
                var kind = (RefreshKind)Interlocked.Exchange(ref _pending, 0);
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
                        account = null;
                        friends = [];
                        requests = new([], []);
                        blocked = [];
                        mailbox = null;
                        kind = RefreshKind.All;
                        if (session is { } id)
                        {
                            connectionStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            connection = new MailConnection(_client, () => Wake(RefreshKind.Mailbox), RefreshFriends,
                                () => { _revoked.Enqueue(id); Refresh(); });
                            connectionTask = connection.RunAsync(Refresh, connectionStop.Token);
                        }
                    }
                    if (session is null)
                    {
                        Publish(null, [], new([], []), null, null, mailboxRefreshed: true);
                        continue;
                    }
                    if ((kind & RefreshKind.Account) != 0 || account is null)
                    {
                        account = await _client.GetMeAsync(ct).ConfigureAwait(false);
                    }
                    PetProfile? pet = Volatile.Read(ref _pet);
                    if (pet is not null && pet != syncedPet)
                    {
                        await _client.UpdatePetAsync(pet, ct).ConfigureAwait(false);
                        syncedPet = pet;
                        account = await _client.GetMeAsync(ct).ConfigureAwait(false);
                    }
                    if ((kind & RefreshKind.Friends) != 0)
                    {
                        friends = await _client.GetFriendsAsync(ct).ConfigureAwait(false);
                        requests = await _client.GetFriendRequestsAsync(ct).ConfigureAwait(false);
                        blocked = await _client.GetBlockedUsersAsync(ct).ConfigureAwait(false);
                    }
                    bool mailboxRefreshed = (kind & RefreshKind.Mailbox) != 0;
                    long mailboxVersion = Volatile.Read(ref _requestedMailboxVersion);
                    if (mailboxRefreshed)
                    {
                        mailbox = await _client.GetNextVisitAsync(ct).ConfigureAwait(false);
                    }
                    Publish(account, friends, requests, mailbox, session, mailboxRefreshed, mailboxVersion,
                        friendsRefreshed: (kind & RefreshKind.Friends) != 0, blockedUsers: blocked);
                    failures = 0;
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    if ((kind & RefreshKind.Friends) != 0)
                    {
                        NotifyFriendsRefresh(false, connectedSession);
                    }
                    if (_client.SessionId is null)
                    {
                        Publish(null, [], new([], []), null, null, mailboxRefreshed: true);
                    }
                    NotifyUnavailable();
                    if (_client.SessionId is not null)
                    {
                        double seconds = Math.Min(60, Math.Pow(2, Math.Min(failures++, 6)));
                        await Task.Delay(TimeSpan.FromSeconds(seconds + Random.Shared.NextDouble()), ct).ConfigureAwait(false);
                        Wake(kind);
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

    internal void Publish(AccountProfile? account, IReadOnlyList<Friend> friends, FriendRequests requests,
        MailboxVisit? mailbox, Guid? session, bool mailboxRefreshed, long mailboxVersion = 0, bool friendsRefreshed = false,
        IReadOnlyList<BlockedUser>? blockedUsers = null)
    {
        _dispatch(() =>
        {
            if (_stop.IsCancellationRequested || session != _client.SessionId)
            {
                return;
            }
            bool sessionChanged = PublishedSessionId != session;
            bool accountChanged = sessionChanged || Account != account;
            IReadOnlyList<BlockedUser> nextBlocked = blockedUsers ?? (sessionChanged ? [] : BlockedUsers);
            bool friendsChanged = sessionChanged || !BlockedUsers.SequenceEqual(nextBlocked) || !Friends.SequenceEqual(friends) ||
                !Requests.Incoming.SequenceEqual(requests.Incoming) || !Requests.Outgoing.SequenceEqual(requests.Outgoing);
            Account = account;
            PublishedSessionId = session;
            Friends = friends;
            Requests = requests;
            BlockedUsers = nextBlocked;
            Mailbox = mailbox;
            if (mailboxRefreshed)
            {
                MailboxVersion = mailboxVersion;
            }
            if (accountChanged)
            {
                AccountChanged?.Invoke();
            }
            if (friendsChanged)
            {
                FriendsChanged?.Invoke();
            }
            if (mailboxRefreshed || friendsChanged || sessionChanged)
            {
                MailboxChanged?.Invoke();
            }
            if (friendsRefreshed)
            {
                FriendsRefreshCompleted?.Invoke(true);
            }
        });
    }
    private void NotifyUnavailable() => _dispatch(() => { if (!_stop.IsCancellationRequested) { _unavailable(); } });

    private void NotifyFriendsRefresh(bool success, Guid? session) => _dispatch(() =>
    {
        if (!_stop.IsCancellationRequested && session == _client.SessionId)
        {
            FriendsRefreshCompleted?.Invoke(success);
        }
    });

    private async Task FallbackAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(10 + Random.Shared.NextDouble() * 5), ct).ConfigureAwait(false);
                Refresh();
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
        _client.SessionChanged -= Refresh;
        await StopAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
    }
}
