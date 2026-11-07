using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Petty.Online.Client;

/// <summary>Owns its HttpClient; serializes session changes and refresh rotation with REST calls.</summary>
public sealed class OnlineClient : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly ITokenStore _store;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly TimeProvider _clock;
    private SessionTokens? _tokens;

    public OnlineClient(Uri server, ITokenStore store, HttpMessageHandler? handler = null, TimeProvider? clock = null)
    {
        Server = ValidateServer(server);
        _store = store;
        _clock = clock ?? TimeProvider.System;
        _http = handler is null
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            : new HttpClient(handler);
        _http.BaseAddress = Server;
        _http.Timeout = TimeSpan.FromSeconds(12);
    }

    public Uri Server
    {
        get;
    }
    public Guid? SessionId => _tokens?.SessionId;
    public event Action? SessionChanged;

    public static Uri ValidateServer(Uri server)
    {
        if (!server.IsAbsoluteUri || !string.IsNullOrEmpty(server.UserInfo) || server.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(server.Query) || !string.IsNullOrEmpty(server.Fragment) ||
            (server.Scheme != "https" && !(server.Scheme == "http" && server.IsLoopback)))
        {
            throw new ArgumentException("Use an HTTPS server origin (HTTP is allowed only on loopback).", nameof(server));
        }
        return server;
    }

    public async Task RestoreAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _tokens = await _store.LoadAsync(ct).ConfigureAwait(false);
            SessionChanged?.Invoke();
        }
        finally { _gate.Release(); }
    }

    public Task RegisterAsync(string login, string password, string confirmPassword, CancellationToken ct = default) =>
        RequestAsync<object>(HttpMethod.Post, "api/v1/auth/register", new
        {
            login,
            password,
            confirmPassword
        }, false, false, ct);

    public async Task LoginAsync(string login, string password, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SessionTokens tokens = (await SendAsync<SessionTokens>(HttpMethod.Post, "api/v1/auth/login", new
            {
                login,
                password
            }, null, false, ct).ConfigureAwait(false))!;
            await SetTokensAsync(tokens, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_tokens is not null)
            {
                try
                {
                    string token = await AccessTokenAsync(false, ct).ConfigureAwait(false);
                    await SendAsync<object>(HttpMethod.Post, "api/v1/auth/logout", null, token, false, ct).ConfigureAwait(false);
                }
                finally { await ClearTokensAsync().ConfigureAwait(false); }
            }
        }
        finally { _gate.Release(); }
    }

    public async Task InvalidateAsync(Guid sessionId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_tokens?.SessionId == sessionId)
            {
                await ClearTokensAsync().ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _tokens is null ? null : await AccessTokenAsync(false, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public Task<AccountProfile> GetMeAsync(CancellationToken ct = default) => RequiredAsync<AccountProfile>(HttpMethod.Get, "api/v1/me", null, ct);
    public Task UpdatePetAsync(PetProfile profile, CancellationToken ct = default) => RequestAsync<object>(HttpMethod.Put, "api/v1/me/pet", profile, true, false, ct);
    public Task<Friend[]> GetFriendsAsync(CancellationToken ct = default) => RequiredAsync<Friend[]>(HttpMethod.Get, "api/v1/friends", null, ct);
    public Task<FriendRequests> GetFriendRequestsAsync(CancellationToken ct = default) => RequiredAsync<FriendRequests>(HttpMethod.Get, "api/v1/friends/requests", null, ct);
    public Task<BlockedUser[]> GetBlockedUsersAsync(CancellationToken ct = default) => RequiredAsync<BlockedUser[]>(HttpMethod.Get, "api/v1/users/blocked", null, ct);
    public Task CancelFriendRequestAsync(Guid id, CancellationToken ct = default) =>
        RequestAsync<object>(HttpMethod.Delete, $"api/v1/friends/requests/{id:D}", null, true, false, ct);
    public Task<LetterHistoryPage> GetConversationAsync(Guid userId, string? cursor = null, int limit = 50, CancellationToken ct = default) =>
        RequiredAsync<LetterHistoryPage>(HttpMethod.Get, $"api/v1/letters/conversations/{userId:D}?limit={limit}&cursor={Uri.EscapeDataString(cursor ?? string.Empty)}", null, ct);
    public async Task<Guid> RequestFriendAsync(string login, CancellationToken ct = default) =>
        (await RequiredAsync<CreatedRequest>(HttpMethod.Post, "api/v1/friends/requests", new
        {
            login
        }, ct)).RequestId;
    public Task RespondToFriendRequestAsync(Guid id, bool accept, CancellationToken ct = default) =>
        RequestAsync<object>(HttpMethod.Post, $"api/v1/friends/requests/{id:D}/{(accept ? "accept" : "decline")}", null, true, false, ct);
    public Task RemoveFriendAsync(Guid id, CancellationToken ct = default) => RequestAsync<object>(HttpMethod.Delete, $"api/v1/friends/{id:D}", null, true, false, ct);
    public Task SetBlockAsync(Guid id, bool blocked, CancellationToken ct = default) =>
        RequestAsync<object>(blocked ? HttpMethod.Post : HttpMethod.Delete, $"api/v1/users/{id:D}/block", null, true, false, ct);
    public Task<MailQuota> GetQuotaAsync(Guid recipient, CancellationToken ct = default) => RequiredAsync<MailQuota>(HttpMethod.Get, $"api/v1/letters/quota/{recipient:D}", null, ct);
    // Caller owns this ID across uncertain responses and explicit retries; never generate it here.
    public async Task<SentLetter> SendLetterAsync(SendLetter letter, CancellationToken ct = default)
    {
        if (letter.ClientRequestId == Guid.Empty)
        {
            throw new ArgumentException("A stable request ID is required.", nameof(letter));
        }
        return (await RequestAsync<SentLetter>(HttpMethod.Post, "api/v1/letters", letter, true, true, ct))!;
    }
    public Task<ReadLettersResult> ReadLettersAsync(Guid[] ids, CancellationToken ct = default) => RequiredAsync<ReadLettersResult>(HttpMethod.Post, "api/v1/letters/read", new { letterIds = ids }, ct);
    public Task<MailboxVisit?> GetNextVisitAsync(CancellationToken ct = default) => RequestAsync<MailboxVisit>(HttpMethod.Get, "api/v1/mailbox/next", null, true, true, ct);
    public Task<LetterHistoryPage> GetHistoryAsync(bool incoming, string? cursor = null, int limit = 50, CancellationToken ct = default) =>
        RequiredAsync<LetterHistoryPage>(HttpMethod.Get, $"api/v1/letters/{(incoming ? "incoming" : "outgoing")}?limit={limit}&cursor={Uri.EscapeDataString(cursor ?? string.Empty)}", null, ct);

    private sealed record CreatedRequest(Guid RequestId);

    private async Task<T> RequiredAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct) where T : class =>
        await RequestAsync<T>(method, path, body, true, method == HttpMethod.Get, ct).ConfigureAwait(false)
        ?? throw new HttpRequestException("Missing response body.");

    private async Task<T?> RequestAsync<T>(HttpMethod method, string path, object? body, bool authenticated, bool retry, CancellationToken ct) where T : class
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string? token = authenticated ? await AccessTokenAsync(false, ct).ConfigureAwait(false) : null;
            try
            {
                return await SendAsync<T>(method, path, body, token, retry, ct).ConfigureAwait(false);
            }
            catch (OnlineException ex) when (authenticated && ex.Status == HttpStatusCode.Unauthorized)
            {
                if (ex.Code == "SESSION_REVOKED")
                {
                    await ClearTokensAsync().ConfigureAwait(false);
                    throw;
                }
                token = await AccessTokenAsync(true, ct).ConfigureAwait(false);
                try
                {
                    return await SendAsync<T>(method, path, body, token, retry, ct).ConfigureAwait(false);
                }
                catch (OnlineException again) when (again.Status == HttpStatusCode.Unauthorized)
                {
                    await ClearTokensAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<string> AccessTokenAsync(bool force, CancellationToken ct)
    {
        SessionTokens tokens = _tokens ?? throw new OnlineException(HttpStatusCode.Unauthorized, "LOGIN_REQUIRED");
        if (!force && tokens.ExpiresAtUtc > _clock.GetUtcNow().AddMinutes(1))
        {
            return tokens.AccessToken;
        }
        try
        {
            SessionTokens refreshed = (await SendAsync<SessionTokens>(HttpMethod.Post, "api/v1/auth/refresh", new
            {
                tokens.RefreshToken
            }, null, false, ct).ConfigureAwait(false))!;
            await SetTokensAsync(refreshed, ct).ConfigureAwait(false);
            return refreshed.AccessToken;
        }
        catch (OnlineException ex) when (ex.Status == HttpStatusCode.Unauthorized)
        {
            await ClearTokensAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task SetTokensAsync(SessionTokens tokens, CancellationToken ct)
    {
        _tokens = tokens;
        try
        {
            await _store.SaveAsync(tokens, ct).ConfigureAwait(false);
        }
        catch { await ClearTokensAsync().ConfigureAwait(false); throw; }
        SessionChanged?.Invoke();
    }

    private async Task ClearTokensAsync()
    {
        _tokens = null;
        try
        {
            await _store.ClearAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally { SessionChanged?.Invoke(); }
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, string? token, bool retry, CancellationToken ct) where T : class
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, path);
                if (body is not null)
                {
                    request.Content = JsonContent.Create(body);
                }
                if (token is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
                using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if (retry && attempt == 0 && (response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(250, 750)), ct).ConfigureAwait(false);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    throw await OnlineException.FromResponseAsync(response, ct).ConfigureAwait(false);
                }
                return response.StatusCode == HttpStatusCode.NoContent ? null : await response.Content.ReadFromJsonAsync<T>(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (retry && attempt == 0 && !ct.IsCancellationRequested && (ex is HttpRequestException or TaskCanceledException))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(250, 750)), ct).ConfigureAwait(false);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
