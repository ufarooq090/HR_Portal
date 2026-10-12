using HrPortal.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HrPortal.Api.Errors;

/// <summary>Maps <see cref="DomainValidationException"/> to a 400 validation problem response.</summary>
public sealed class DomainValidationExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainValidationException validation)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [validation.Field] = [validation.Message],
            })
            {
                Status = StatusCodes.Status400BadRequest,
            },
            Exception = exception,
        });
    }
}
