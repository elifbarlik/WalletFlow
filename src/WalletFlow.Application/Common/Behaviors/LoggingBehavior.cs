using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace WalletFlow.Application.Common.Behaviors;

/// <summary>Logs request name and duration only. Payloads are never logged because they may hold secrets.</summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await next();
            logger.LogInformation("Handled {RequestName} in {ElapsedMs} ms", name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch (Exception ex) when (ex is AppException or FluentValidation.ValidationException)
        {
            logger.LogInformation("{RequestName} rejected after {ElapsedMs} ms: {Reason}", name, Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex.GetType().Name);
            throw;
        }
    }
}
