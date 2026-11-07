using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Petty.Server.Api.Errors;

namespace Petty.Server.Tests;

public sealed class ApiExceptionHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrappedWriteFailuresReturnRetryableDatabaseError(bool executionStrategyWrapper)
    {
        Exception failure = new DbUpdateException("Write failed", new NpgsqlException("Connection failed", new TimeoutException()));
        if (executionStrategyWrapper)
        {
            failure = new InvalidOperationException("Transient failure", failure);
        }

        using ServiceProvider services = new ServiceCollection().AddOptions().BuildServiceProvider();
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = body;
        Assert.True(await new ApiExceptionHandler().TryHandleAsync(context, failure, CancellationToken.None));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        body.Position = 0;
        using JsonDocument response = await JsonDocument.ParseAsync(body);
        Assert.Equal("DATABASE_UNAVAILABLE", response.RootElement.GetProperty("code").GetString());
    }
}
