using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WalletFlow.Application.Common;
using WalletFlow.Domain.Common;

namespace WalletFlow.Api.Infrastructure;

/// <summary>Single place that turns exceptions into RFC 7807 problem responses. Internals are never leaked.</summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problems, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, code) = Map(exception);
        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception");
        }
        else if (status != StatusCodes.Status401Unauthorized)
        {
            logger.LogInformation("Request rejected with {Status} ({Code})", status, code);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = Detail(exception, status),
            Type = $"https://httpstatuses.io/{status}",
        };
        problem.Extensions["code"] = code;
        if (exception is ValidationException validation)
        {
            problem.Extensions["errors"] = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        }

        context.Response.StatusCode = status;
        if (exception is ExternalServiceException)
        {
            context.Response.Headers.RetryAfter = "10";
        }

        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }

    private static (int Status, string Title, string Code) Map(Exception exception) => exception switch
    {
        ValidationException => (StatusCodes.Status400BadRequest, "Validation failed", "validation.failed"),
        BadHttpRequestException => (StatusCodes.Status400BadRequest, "Malformed request", "request.malformed"),
        DomainException e => (StatusCodes.Status422UnprocessableEntity, "Business rule violated", e.Code),
        IdempotencyKeyReusedException e => (StatusCodes.Status422UnprocessableEntity, "Idempotency key reused", e.Code),
        AuthenticationFailedException e => (StatusCodes.Status401Unauthorized, "Authentication failed", e.Code),
        ForbiddenException e => (StatusCodes.Status403Forbidden, "Forbidden", e.Code),
        NotFoundException e => (StatusCodes.Status404NotFound, "Not found", e.Code),
        PaymentDeclinedException e => (StatusCodes.Status402PaymentRequired, "Payment declined", e.Code),
        ExternalServiceException e => (StatusCodes.Status503ServiceUnavailable, "Dependency unavailable", e.Code),
        AppException e => (StatusCodes.Status409Conflict, "Conflict", e.Code),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "server.error"),
    };

    private static string Detail(Exception exception, int status) => exception switch
    {
        ValidationException => "One or more validation errors occurred.",
        BadHttpRequestException => "The request could not be parsed.",
        _ when status >= 500 => "An unexpected error occurred. Quote the traceId when contacting support.",
        _ => exception.Message,
    };
}
