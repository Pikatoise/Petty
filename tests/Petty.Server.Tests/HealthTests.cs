using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Petty.Server.Application.Features.Auth;
using Petty.Server.Application.Health;
using Petty.Server.Application.Persistence;
using Petty.Server.Infrastructure.Persistence;

namespace Petty.Server.Tests;

public sealed class HealthTests
{
    [Theory]
    [InlineData(true, HttpStatusCode.OK, "ready")]
    [InlineData(false, HttpStatusCode.ServiceUnavailable, "not_ready")]
    public async Task HealthEndpointsSeparateProcessAndDatabaseReadiness(bool ready, HttpStatusCode expected, string status)
    {
        using var factory = new ServerFactory(ready);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("{\"status\":\"alive\"}", await live.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.ReadinessChecks);

        using HttpResponseMessage readiness = await client.GetAsync("/health/ready");
        Assert.Equal(expected, readiness.StatusCode);
        Assert.Equal($"{{\"status\":\"{status}\"}}", await readiness.Content.ReadAsStringAsync());
        Assert.Equal(1, factory.ReadinessChecks);
    }

    [Fact]
    public async Task AuthRateLimitRejectsRepeatedRequestsBeforeDatabaseWork()
    {
        using var factory = new ServerFactory(true, 1);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage first = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
        });
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        using HttpResponseMessage second = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        JsonElement error = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RATE_LIMITED", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task DatabaseUnavailabilityDoesNotReportSessionRevocation()
    {
        using var factory = new ServerFactory(true);
        using HttpClient client = factory.CreateClient();
        IAccessTokenIssuer issuer = factory.Services.GetRequiredService<IAccessTokenIssuer>();
        string token = issuer.Issue(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        JsonElement error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DATABASE_UNAVAILABLE", error.GetProperty("code").GetString());
    }

    [Fact]
    public void MediatorAndDatabaseAreScoped()
    {
        using var factory = new ServerFactory(true);
        using IServiceScope first = factory.Services.CreateScope();
        using IServiceScope second = factory.Services.CreateScope();

        Assert.Same(first.ServiceProvider.GetRequiredService<IMediator>(), first.ServiceProvider.GetRequiredService<IMediator>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IMediator>(), second.ServiceProvider.GetRequiredService<IMediator>());
        Assert.Same(first.ServiceProvider.GetRequiredService<ServerDbContext>(), first.ServiceProvider.GetRequiredService<ServerDbContext>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<ServerDbContext>(), second.ServiceProvider.GetRequiredService<ServerDbContext>());
        Assert.Same(first.ServiceProvider.GetRequiredService<ServerDbContext>(), first.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    private sealed class ServerFactory(bool ready, int rateLimit = 20) : WebApplicationFactory<Program>
    {
        public int ReadinessChecks
        {
            get; private set;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Petty", "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1");
            builder.UseSetting("Auth:SigningKey", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Auth:RequestsPerMinute", rateLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDatabaseReadiness>();
                services.AddScoped<IDatabaseReadiness>(_ => new FixedReadiness(() =>
                {
                    ReadinessChecks++;
                    return ready;
                }));
            });
        }
    }

    private sealed class FixedReadiness(Func<bool> check) : IDatabaseReadiness
    {
        public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(check());
    }
}
