using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Petty.Server.Api.Errors;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Infrastructure.Identity;
using Petty.Server.Infrastructure.Persistence;

namespace Petty.Server.Api.Auth;

public static class AuthenticationSetup
{
    public static void AddOnlineAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        byte[] signingKey;
        try
        {
            signingKey = Convert.FromBase64String(configuration["Auth:SigningKey"] ?? string.Empty);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Auth__SigningKey must be a base64-encoded key of at least 32 random bytes.");
        }

        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException("Configure Auth__SigningKey with at least 32 random bytes encoded as base64.");
        }

        var settings = new JwtSettings(configuration["Auth:Issuer"] ?? "Petty.Server",
            configuration["Auth:Audience"] ?? "Petty.Desktop", new SymmetricSecurityKey(signingKey));
        services.AddSingleton(settings);
        services.AddSingleton<IAccessTokenIssuer, AccessTokenIssuer>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";
            options.Password.RequiredLength = 12;
            options.Password.RequireNonAlphanumeric = false;
        }).AddUserStore<ApplicationUserStore>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = settings.Issuer,
                ValidateAudience = true,
                ValidAudience = settings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = settings.Key,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context => ApiExceptionHandler.IsDatabaseUnavailable(context.Exception)
                    ? Task.FromException(context.Exception) : Task.CompletedTask,
                OnTokenValidated = async context =>
                {
                    ISessionRepository sessions = context.HttpContext.RequestServices.GetRequiredService<ISessionRepository>();
                    TimeProvider clock = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                    if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out Guid userId)
                        || !Guid.TryParse(context.Principal?.FindFirst("sid")?.Value, out Guid sessionId))
                    {
                        context.Fail("UNAUTHORIZED");
                        return;
                    }

                    bool active = await sessions.IsActiveAsync(userId, sessionId, clock.GetUtcNow(), context.HttpContext.RequestAborted);
                    // A valid signed token can only terminate its own session again; it grants no data access.
                    bool logout = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<AllowRevokedSessionForLogoutAttribute>() is not null;
                    if (!active && !logout)
                    {
                        context.HttpContext.Items["AuthErrorCode"] = "SESSION_REVOKED";
                        context.Fail("SESSION_REVOKED");
                    }
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await context.Response.WriteAsJsonAsync(new ProblemDetails
                    {
                        Status = StatusCodes.Status401Unauthorized,
                        Title = "Unauthorized",
                        Extensions = { ["code"] = context.HttpContext.Items["AuthErrorCode"] ?? "UNAUTHORIZED" }
                    }, options: null, contentType: "application/problem+json", cancellationToken: context.HttpContext.RequestAborted);
                }
            };
        });
        services.AddAuthorization();
    }
}
