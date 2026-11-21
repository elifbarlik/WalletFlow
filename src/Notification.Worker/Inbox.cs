using StackExchange.Redis;

namespace Notification.Worker;

/// <summary>
/// Inbox pattern: remembers which message ids were already handled, so at-least-once delivery from the outbox
/// never produces a duplicate notification.
/// </summary>
internal interface IInbox
{
    /// <summary>Returns true if this worker may process the message, false if it was already handled or is being handled.</summary>
    Task<bool> TryBeginAsync(string messageId);

    /// <summary>Forgets the message so a redelivery can try again (used when processing failed).</summary>
    Task ForgetAsync(string messageId);
}

/// <summary>Survives restarts and is shared by every worker replica.</summary>
internal sealed class RedisInbox(IConnectionMultiplexer redis) : IInbox
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    public Task<bool> TryBeginAsync(string messageId) =>
        redis.GetDatabase().StringSetAsync($"inbox:{messageId}", "1", Retention, When.NotExists);

    public async Task ForgetAsync(string messageId) => await redis.GetDatabase().KeyDeleteAsync($"inbox:{messageId}");
}

/// <summary>Fallback for local runs without Redis: correct for one process, lost on restart.</summary>
internal sealed class InMemoryInbox : IInbox
{
    private readonly HashSet<string> _seen = [];

    public Task<bool> TryBeginAsync(string messageId)
    {
        lock (_seen)
        {
            return Task.FromResult(_seen.Add(messageId));
        }
    }

    public Task ForgetAsync(string messageId)
    {
        lock (_seen)
        {
            _seen.Remove(messageId);
        }

        return Task.CompletedTask;
    }
}
