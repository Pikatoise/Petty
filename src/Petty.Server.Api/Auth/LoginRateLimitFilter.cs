using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Petty.Server.Api.Controllers;

namespace Petty.Server.Api.Auth;

/// <summary>Bounds password work for one normalized login across different client IPs.</summary>
public sealed class LoginRateLimitFilter : IAsyncActionFilter, IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public LoginRateLimitFilter(IConfiguration configuration)
    {
        int limit = Math.Clamp(configuration.GetValue("Auth:LoginRequestsPerMinute", 10), 1, 1000);
        _limiter = PartitionedRateLimiter.Create<string, string>(login => RateLimitPartition.GetFixedWindowLimiter(login,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        string? login = context.ActionArguments.Values.Select(value => value switch
        {
            LoginRequest request => request.Login,
            RegisterRequest request => request.Login,
            _ => null
        }).FirstOrDefault(value => value is not null)?.Trim();
        if (login is not { Length: >= 3 and <= 32 } || login.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
        {
            await next();
            return;
        }
        using RateLimitLease lease = _limiter.AttemptAcquire(login.ToUpperInvariant());
        if (!lease.IsAcquired)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Extensions = { ["code"] = "RATE_LIMITED" }
            })
            {
                StatusCode = StatusCodes.Status429TooManyRequests
            };
            return;
        }
        await next();
    }

    public void Dispose() => _limiter.Dispose();
}
