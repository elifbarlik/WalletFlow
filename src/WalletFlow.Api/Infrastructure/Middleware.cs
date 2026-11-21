using System.Text.RegularExpressions;
using Serilog.Context;

namespace WalletFlow.Api.Infrastructure;

/// <summary>
/// Propagates a correlation id through logs and responses. Inbound values are accepted only if they look like an id,
/// which prevents log injection through a crafted header.
/// </summary>
internal sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var inbound = context.Request.Headers[HeaderName].ToString();
        var correlationId = SafeId().IsMatch(inbound) ? inbound : Guid.NewGuid().ToString("N");
        context.Response.Headers[HeaderName] = correlationId;
        context.TraceIdentifier = correlationId;
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
    private static partial Regex SafeId();
}

/// <summary>Defensive response headers for a JSON API. Swagger UI is exempt from the strict CSP.</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            if (!context.Request.Path.StartsWithSegments("/swagger"))
            {
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                headers["Cache-Control"] = "no-store";
            }

            return Task.CompletedTask;
        });
        return next(context);
    }
}
