using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace WalletFlow.Api.Infrastructure;

/// <summary>
/// Unhealthy when Redis is the idempotency store (money endpoints would fail closed), otherwise only Degraded
/// because Redis then merely backs rate limiting, which fails open.
/// </summary>
internal sealed class RedisHealthCheck(IConnectionMultiplexer redis, IConfiguration config) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var critical = string.Equals(config["Idempotency:Provider"], "Redis", StringComparison.OrdinalIgnoreCase);
        try
        {
            var latency = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"PING {latency.TotalMilliseconds:0.0} ms");
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return critical ? HealthCheckResult.Unhealthy("Redis is unreachable.") : HealthCheckResult.Degraded("Redis is unreachable.");
        }
    }
}
