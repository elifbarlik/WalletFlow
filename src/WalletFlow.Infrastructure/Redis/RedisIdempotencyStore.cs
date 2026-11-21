using System.Text.Json;
using StackExchange.Redis;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;

namespace WalletFlow.Infrastructure.Redis;

/// <summary>
/// Low-latency idempotency store. Reservation is a single atomic <c>SET key value NX EX</c>; TTLs replace the
/// "stale reservation" bookkeeping of the SQL store. Redis is a cache here, not the source of truth: if it loses
/// data, the primary key of the money transaction (derived from the key) still prevents double execution.
/// It fails closed: when Redis is unreachable, money-moving requests are rejected with 503 instead of risking duplicates.
/// </summary>
internal sealed class RedisIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IdempotencyEntry?> FindAsync(Guid userId, string key, CancellationToken ct)
    {
        var value = await Guard(() => redis.GetDatabase().StringGetAsync(KeyOf(userId, key)));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<IdempotencyEntry>((string)value!, Json);
    }

    public Task<bool> TryReserveAsync(Guid userId, string key, IdempotencyEntry entry, CancellationToken ct) =>
        Guard(() => redis.GetDatabase().StringSetAsync(
            KeyOf(userId, key), JsonSerializer.Serialize(entry, Json), RedisOptions.InProgressTtl, When.NotExists));

    public async Task CompleteAsync(Guid userId, string key, string responseJson, CancellationToken ct)
    {
        var current = await FindAsync(userId, key, ct);
        if (current is null)
        {
            return; // expired while the request was running; the money transaction id still protects against replays
        }

        var completed = current with { IsCompleted = true, ResponseJson = responseJson };
        await Guard(() => redis.GetDatabase().StringSetAsync(
            KeyOf(userId, key), JsonSerializer.Serialize(completed, Json), RedisOptions.CompletedTtl, When.Exists));
    }

    public async Task ReleaseAsync(Guid userId, string key, CancellationToken ct) =>
        await Guard(() => redis.GetDatabase().KeyDeleteAsync(KeyOf(userId, key)));

    private static string KeyOf(Guid userId, string key) => $"idem:{userId:N}:{key}";

    private static async Task<T> Guard<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            throw new ExternalServiceException("The idempotency store is unavailable. Retry shortly.");
        }
    }
}
