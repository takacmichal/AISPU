using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SolarOps.Api.Infrastructure.Errors;

/// <summary>
/// Maps known exceptions to RFC 7807 problem details:
/// <see cref="DomainException"/> → 409, <see cref="NotFoundException"/> → 404,
/// <see cref="RequestValidationException"/> → 400, <see cref="UnauthorizedAccessException"/> → 401
/// (e.g. <c>ICurrentUser.RequireUserId()</c> on an anonymous request). Anything else falls through to the default 500 handler.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            RequestValidationException ve => new ValidationProblemDetails(ve.Errors)
            {
                Status = StatusCodes.Status400BadRequest,
            },
            NotFoundException => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = exception.Message,
            },
            DomainException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = exception.Message,
            },
            UnauthorizedAccessException => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
            },
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        logger.LogInformation("Handled {ExceptionType}: {Message}", exception.GetType().Name, exception.Message);

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
