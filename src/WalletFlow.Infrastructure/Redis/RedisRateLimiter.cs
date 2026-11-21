using System.Diagnostics.CodeAnalysis;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace WalletFlow.Infrastructure.Redis;

/// <summary>Creates rate limiters whose counters live in Redis, so limits hold across every API replica.</summary>
public interface IDistributedRateLimiterFactory
{
    RateLimiter CreateFixedWindow(string key, int permitLimit, TimeSpan window);
}

internal sealed class RedisRateLimiterFactory(IConnectionMultiplexer redis, ILogger<RedisRateLimiterFactory> logger)
    : IDistributedRateLimiterFactory
{
    public RateLimiter CreateFixedWindow(string key, int permitLimit, TimeSpan window) =>
        new RedisFixedWindowLimiter(redis, logger, key, permitLimit, window);
}

/// <summary>
/// Fixed-window counter: one Lua script increments the window key and sets its expiry atomically.
/// Availability beats strictness here: if Redis is unreachable the request is allowed (and logged), because
/// rate limiting is a protection layer and must not take the API down with it.
/// </summary>
internal sealed class RedisFixedWindowLimiter(
    IConnectionMultiplexer redis, ILogger logger, string key, int permitLimit, TimeSpan window) : RateLimiter
{
    private const string Script = "local c = redis.call('INCR', KEYS[1]) if c == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end return c";

    public override TimeSpan? IdleDuration => window;

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount) => Evaluate(permitCount);

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        try
        {
            var windowId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / (long)window.TotalMilliseconds;
            var count = (long)await redis.GetDatabase().ScriptEvaluateAsync(
                Script, [new RedisKey($"rl:{key}:{windowId}")], [(long)window.TotalMilliseconds]);
            return Lease(count);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Rate-limit store unavailable; allowing the request");
            return Lease(0);
        }
    }

    private RateLimitLease Evaluate(int permitCount) => AcquireAsyncCore(permitCount, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    private RedisLease Lease(long count) =>
        count <= permitLimit ? new RedisLease(true, null) : new RedisLease(false, window);

    private sealed class RedisLease(bool acquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => acquired;

        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, [NotNullWhen(true)] out object? metadata)
        {
            if (metadataName == MetadataName.RetryAfter.Name && retryAfter is { } value)
            {
                metadata = value;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
