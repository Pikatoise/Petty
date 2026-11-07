using System.Threading.RateLimiting;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Petty.Server.Api.Auth;
using Petty.Server.Api.Errors;
using Petty.Server.Api.Hosting;
using Petty.Server.Api.Realtime;
using Petty.Server.Application.Features.Delivery;
using Petty.Server.Application.Features.Friends;
using Petty.Server.Application.Features.Letters;
using Petty.Server.Application.Health;
using Petty.Server.Application.Notifications;
using Petty.Server.Application.Persistence;
using Petty.Server.Infrastructure.Persistence;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServerHosting();

string? connectionString = builder.Configuration.GetConnectionString("Petty");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Configure ConnectionStrings__Petty before starting the server.");
}

builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Invalid request",
        Extensions = { ["code"] = "INVALID_REQUEST" }
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOnlineAuthentication(builder.Configuration);
builder.Services.AddSingleton<LoginRateLimitFilter>();
builder.Services.AddScoped<IFriendshipRepository, FriendshipRepository>();
builder.Services.AddScoped<ILetterRepository, LetterRepository>();
builder.Services.AddScoped<IMailboxRepository, MailboxRepository>();
builder.Services.AddScoped<IMailNotifier, SignalRMailNotifier>();
builder.Services.AddSingleton<IUserIdProvider, SessionUserIdProvider>();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 4096;
    options.AddFilter<SessionHubFilter>();
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Clamp(builder.Configuration.GetValue("Auth:RequestsPerMinute", 20), 1, 1000),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("friends", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Clamp(builder.Configuration.GetValue("Friends:RequestsPerMinute", 20), 1, 1000),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("letters", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Clamp(builder.Configuration.GetValue("Letters:RequestsPerMinute", 30), 1, 1000),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests",
            Extensions = { ["code"] = "RATE_LIMITED" }
        }, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
    };
});
builder.Services.AddDbContext<ServerDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IUnitOfWork>(services => services.GetRequiredService<ServerDbContext>());
builder.Services.AddScoped<IDatabaseReadiness, DatabaseReadiness>();
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(GetReadiness)];
});

WebApplication app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseServerHosting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers().WithRequestTimeout("rest");
app.MapHub<MailHub>("/hubs/mail", options => options.CloseOnAuthenticationExpiration = true);
app.Run();

public partial class Program;
