using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Petty.Server.Application.Health;

namespace Petty.Server.Tests;

public sealed class HostingTests
{
    [Fact]
    public async Task OnlyConfiguredProxyCanSupplyHttpsAndClientAddress()
    {
        using var factory = new HostingFactory(requireHttps: true, ipLimit: 1);
        Assert.Equal(400, await Post(factory, "127.0.0.8", "198.51.100.1")); // Untrusted HTTPS header.
        Assert.Equal(400, await Post(factory, "127.0.0.9", "198.51.100.1")); // Trusted, invalid model.
        Assert.Equal(429, await Post(factory, "127.0.0.9", "198.51.100.1"));
        Assert.Equal(400, await Post(factory, "127.0.0.9", "198.51.100.2")); // Separate real client budget.
        using HttpClient client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task ForgedForwardedAddressCannotResetIpBudget()
    {
        using var factory = new HostingFactory(requireHttps: false, ipLimit: 1);
        Assert.Equal(400, await Post(factory, "127.0.0.8", "198.51.100.1"));
        Assert.Equal(429, await Post(factory, "127.0.0.8", "198.51.100.2"));
    }

    [Fact]
    public async Task LoginBudgetNormalizesCaseAndWhitespaceAcrossClientAddresses()
    {
        using var factory = new HostingFactory(requireHttps: true);
        Assert.Equal(503, await Post(factory, "127.0.0.9", "198.51.100.1", "/api/v1/auth/login",
            "{\"login\":\"Example_User\",\"password\":\"InvalidPassword123\"}"));
        Assert.Equal(429, await Post(factory, "127.0.0.9", "198.51.100.2", "/api/v1/auth/login",
            "{\"login\":\" example_user \",\"password\":\"InvalidPassword123\"}"));
    }

    [Fact]
    public async Task RestDeadlineCancelsWorkAndReturnsSafeTimeout()
    {
        using var factory = new HostingFactory(slow: true);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Contains("REQUEST_TIMEOUT", await response.Content.ReadAsStringAsync());
    }

    private static async Task<int> Post(HostingFactory factory, string peer, string forwarded,
        string path = "/api/v1/auth/register", string body = "{}")
    {
        // Initialize the host before configuring the transport peer on TestServer.
        _ = factory.CreateClient();
        HttpContext response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Method = "POST";
            context.Request.Path = path;
            context.Request.Host = new HostString("example.test");
            context.Request.Headers["X-Forwarded-For"] = forwarded;
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = bytes.Length;
        });
        return response.Response.StatusCode;
    }

    private sealed class HostingFactory(bool requireHttps = false, int ipLimit = 1000, bool slow = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Petty", "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1");
            builder.UseSetting("Auth:SigningKey", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Auth:RequestsPerMinute", ipLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("Auth:LoginRequestsPerMinute", "1");
            builder.UseSetting("AllowedHosts", "example.test;localhost");
            builder.UseSetting("Hosting:RequireHttps", requireHttps.ToString());
            builder.UseSetting("Hosting:KnownProxies:0", "127.0.0.9");
            builder.UseSetting("Hosting:RequestTimeoutSeconds", slow ? "1" : "15");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDatabaseReadiness>();
                services.AddScoped<IDatabaseReadiness>(_ => new Readiness(slow));
            });
        }
    }

    private sealed class Readiness(bool slow) : IDatabaseReadiness
    {
        public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            if (slow)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return true;
        }
    }
}
