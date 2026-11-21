using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Infrastructure.Redis;

namespace WalletFlow.Api.Infrastructure;

internal static class RateLimiting
{
    public const string Auth = "auth";
    public const string Money = "money";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var scale = config.GetValue("RateLimiting:Scale", 1);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Global safety net per authenticated user, falling back to the client IP.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetSlidingWindowLimiter(PartitionKey(ctx), _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 120 * scale,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0,
                }));

            // Credential-stuffing protection: strict, per client IP.
            options.AddPolicy(Auth, ctx => Fixed(ctx, $"auth:{Ip(ctx)}", 10 * scale, TimeSpan.FromMinutes(1)));

            // Money movement: per authenticated user.
            options.AddPolicy(Money, ctx => Fixed(ctx, $"money:{PartitionKey(ctx)}", 30 * scale, TimeSpan.FromMinutes(1)));
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ProblemDetails { Status = 429, Title = "Too many requests", Detail = "Rate limit exceeded. Slow down and retry later." },
                    ct);
            };
        });
        return services;
    }

    /// <summary>
    /// Uses Redis counters when Redis is configured (limits then hold across all replicas); otherwise an
    /// in-process window, which is only exact for a single instance.
    /// </summary>
    private static RateLimitPartition<string> Fixed(HttpContext ctx, string key, int permit, TimeSpan window)
    {
        var distributed = ctx.RequestServices.GetService<IDistributedRateLimiterFactory>();
        return distributed is null
            ? RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = window, QueueLimit = 0 })
            : RateLimitPartition.Get(key, k => distributed.CreateFixedWindow(k, permit, window));
    }

    private static string PartitionKey(HttpContext ctx) => ctx.User.FindFirst("sub")?.Value is { } sub ? $"u:{sub}" : $"ip:{Ip(ctx)}";

    private static string Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

