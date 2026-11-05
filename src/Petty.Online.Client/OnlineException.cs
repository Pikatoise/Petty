using System.Net;
using System.Text.Json;

namespace Petty.Online.Client;

public sealed class OnlineException(HttpStatusCode status, string code, int? unreadCount = null, int? limit = null)
    : Exception(code)
{
    public HttpStatusCode Status { get; } = status;
    public string Code { get; } = code;
    public int? UnreadCount { get; } = unreadCount;
    public int? Limit { get; } = limit;

    internal static async Task<OnlineException> FromResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            JsonElement root = json.RootElement;
            string code = root.TryGetProperty("code", out JsonElement value) ? value.GetString() ?? "REQUEST_FAILED" : "REQUEST_FAILED";
            return new OnlineException(response.StatusCode, code,
                root.TryGetProperty("unreadCount", out value) && value.TryGetInt32(out int count) ? count : null,
                root.TryGetProperty("limit", out value) && value.TryGetInt32(out int limit) ? limit : null);
        }
        catch (JsonException)
        {
            return new OnlineException(response.StatusCode, "REQUEST_FAILED");
        }
    }
}
