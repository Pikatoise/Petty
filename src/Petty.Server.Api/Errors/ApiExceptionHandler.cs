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
                FailureKind.Forbidden => StatusCodes.Status403Forbidden,
                FailureKind.NotFound => StatusCodes.Status404NotFound,
                FailureKind.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError
            };
            code = failure.Code;
        }
        else if (exception is DbUpdateException
        {
            InnerException: PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        } && postgres.ConstraintName is "UserNameIndex" or "IX_Letters_SenderId_ClientRequestId")
        {
            status = StatusCodes.Status409Conflict;
            code = postgres.ConstraintName == "UserNameIndex" ? "USERNAME_TAKEN" : "IDEMPOTENCY_KEY_REUSED";
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
        var problem = new ProblemDetails
        {
            Status = status,
            Title = "Request failed",
            Extensions = { ["code"] = code }
        };
        if (exception is RequestFailure { Details: { } details })
        {
            foreach (KeyValuePair<string, object?> detail in details)
            {
                problem.Extensions[detail.Key] = detail.Value;
            }
        }

        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
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
