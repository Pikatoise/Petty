using System.Net;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.HttpOverrides;

namespace Petty.Server.Api.Hosting;

public static class HostingSetup
{
    public static void AddServerHosting(this WebApplicationBuilder builder)
    {
        // Secret files use the same double-underscore keys as environment variables.
        builder.Configuration.AddKeyPerFile("/run/petty", optional: true);
        bool requireHttps = builder.Configuration.GetValue("Hosting:RequireHttps", false);
        string[] proxies = builder.Configuration.GetSection("Hosting:KnownProxies").Get<string[]>() ?? [];
        if (requireHttps && (proxies.Length == 0 ||
            string.IsNullOrWhiteSpace(builder.Configuration["AllowedHosts"]) ||
            builder.Configuration["AllowedHosts"]!.Split(';').Any(host => host.Trim() == "*")))
        {
            throw new InvalidOperationException("HTTPS hosting requires explicit KnownProxies and AllowedHosts.");
        }
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (string proxy in proxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
            options.ForwardedHeaders = proxies.Length == 0 ? ForwardedHeaders.None
                : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.RequireHeaderSymmetry = true;
        });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 16 * 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(60);
            options.Limits.MaxConcurrentConnections = Math.Clamp(builder.Configuration.GetValue("Hosting:MaxConnections", 256), 1, 10000);
            options.Limits.MaxConcurrentUpgradedConnections = Math.Clamp(builder.Configuration.GetValue("Hosting:MaxWebSockets", 128), 1, 10000);
        });
        builder.Services.AddRequestTimeouts(options => options.AddPolicy("rest", new RequestTimeoutPolicy
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(builder.Configuration.GetValue("Hosting:RequestTimeoutSeconds", 15), 1, 120)),
            TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
            WriteTimeoutResponse = async context => await context.Response.WriteAsJsonAsync(new
            {
                status = StatusCodes.Status504GatewayTimeout,
                title = "Request timed out",
                code = "REQUEST_TIMEOUT"
            })
        }));
    }

    public static void UseServerHosting(this WebApplication app)
    {
        app.UseForwardedHeaders();
        if (app.Configuration.GetValue("Hosting:RequireHttps", false))
        {
            app.Use(async (context, next) =>
            {
                // Liveness is available to local container probes without proxying through TLS.
                if (!context.Request.IsHttps && context.Request.Path != "/health/live")
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        status = 400,
                        code = "HTTPS_REQUIRED"
                    });
                    return;
                }
                await next(context);
            });
        }
        app.UseRouting();
        app.UseRequestTimeouts();
    }
}
