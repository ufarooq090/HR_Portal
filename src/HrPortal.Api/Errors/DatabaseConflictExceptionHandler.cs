using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HrPortal.Api.Errors;

/// <summary>Maps unique and foreign-key violations to 409 Conflict instead of a 500.</summary>
public sealed class DatabaseConflictExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateException { InnerException: PostgresException postgres })
        {
            return false;
        }

        var title = postgres.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation => "Another record already uses this name or code.",
            PostgresErrorCodes.ForeignKeyViolation => "This record is in use by other records. Deactivate it instead of deleting it.",
            _ => null,
        };

        if (title is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = title },
            Exception = exception,
        });
    }
}
