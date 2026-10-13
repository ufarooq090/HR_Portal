using HrPortal.Infrastructure.Templates;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HrPortal.Api.Errors;

/// <summary>Maps the email service's refusals: a missing template or record is 404, a refused send 403 with the reason.</summary>
public sealed class EmailExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            EmailNotFoundException => StatusCodes.Status404NotFound,
            EmailDeniedException => StatusCodes.Status403Forbidden,
            _ => 0,
        };
        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Detail = exception.Message },
            Exception = exception,
        });
    }
}
