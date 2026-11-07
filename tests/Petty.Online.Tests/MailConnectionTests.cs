using System.Net;
using Microsoft.AspNetCore.Http.Connections;
using Petty.Online.Client;

namespace Petty.Online.Tests;

public sealed class MailConnectionTests
{
    [Fact]
    public async Task CancellingTransportStartupCompletesWithoutFaultingTheConnectionWorker()
    {
        var store = new OnlineClientTests.MemoryStore { Tokens = OnlineClientTests.Tokens() };
        await using var client = new OnlineClient(new Uri("https://example.test"), store);
        await client.RestoreAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        await using var connection = new MailConnection(client, () => { }, () => { }, options =>
        {
            options.Transports = HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => new OnlineClientTests.Handler(async (request, ct) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/negotiate", StringComparison.Ordinal))
                {
                    return OnlineClientTests.Json(new
                    {
                        negotiateVersion = 1,
                        connectionId = "test",
                        connectionToken = "test",
                        availableTransports = new[]
                        {
                            new { transport = "ServerSentEvents", transferFormats = new[] { "Text" } },
                            new { transport = "LongPolling", transferFormats = new[] { "Text", "Binary" } }
                        }
                    });
                }
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        });
        Task worker = connection.RunAsync(() => { }, stop.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await worker.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
