using Microsoft.AspNetCore.SignalR;
using Petty.Server.Application.Features.Auth;

namespace Petty.Server.Api.Realtime;

public sealed class SessionUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        Guid.TryParse(connection.User?.FindFirst("sid")?.Value, out Guid sessionId) ? sessionId.ToString("N") : null;
}

public sealed class SessionHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocation, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        await Validate(invocation.Context, invocation.ServiceProvider);
        return await next(invocation);
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        await Validate(context.Context, context.ServiceProvider);
        await next(context);
    }

    private static async Task Validate(HubCallerContext context, IServiceProvider services)
    {
        ISessionRepository sessions = services.GetRequiredService<ISessionRepository>();
        TimeProvider clock = services.GetRequiredService<TimeProvider>();
        if (!Guid.TryParse(context.User?.FindFirst("sub")?.Value, out Guid userId)
            || !Guid.TryParse(context.User?.FindFirst("sid")?.Value, out Guid sessionId)
            || !await sessions.IsActiveAsync(userId, sessionId, clock.GetUtcNow(), context.ConnectionAborted))
        {
            context.Abort();
            throw new HubException("SESSION_REVOKED");
        }
    }
}
