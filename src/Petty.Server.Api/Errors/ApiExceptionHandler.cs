using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Petty.Server.Application;

namespace Petty.Server.Api.Errors;

public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        int status;
        string code;
        if (exception is RequestFailure failure)
        {
            status = failure.Kind switch
            {
                FailureKind.Invalid => StatusCodes.Status400BadRequest,
                FailureKind.Unauthorized => StatusCodes.Status401Unauthorized,
                FailureKind.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError
            };
            code = failure.Code;
        }
        else if (exception is DbUpdateException
        {
            InnerException: PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UserNameIndex" }
        })
        {
            status = StatusCodes.Status409Conflict;
            code = "USERNAME_TAKEN";
        }
        else if (IsDatabaseUnavailable(exception))
        {
            status = StatusCodes.Status503ServiceUnavailable;
            code = "DATABASE_UNAVAILABLE";
        }
        else
        {
            return false;
        }

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = "Request failed",
            Extensions = { ["code"] = code }
        }, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }

    internal static bool IsDatabaseUnavailable(Exception exception)
    {
        while ((exception is DbUpdateException || exception is InvalidOperationException) && exception.InnerException is { } inner)
        {
            exception = inner;
        }

        return exception is TimeoutException or NpgsqlException { IsTransient: true };
    }
}
