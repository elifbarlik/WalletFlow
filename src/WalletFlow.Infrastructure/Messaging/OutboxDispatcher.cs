using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WalletFlow.Application.Abstractions;
using WalletFlow.Infrastructure.Persistence;

namespace WalletFlow.Infrastructure.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync(Guid messageId, string type, string payload, CancellationToken ct);
}

/// <summary>Default publisher: records that an event was emitted. Swap for a broker implementation in production.</summary>
internal sealed class LoggingMessagePublisher(ILogger<LoggingMessagePublisher> logger) : IMessagePublisher
{
    public Task PublishAsync(Guid messageId, string type, string payload, CancellationToken ct)
    {
        logger.LogInformation("Published integration event {EventType} ({MessageId})", type, messageId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Transactional-outbox relay. Events are written in the same DB transaction as the business change, so they
/// cannot be lost; this service delivers them at-least-once. Several instances can run concurrently thanks to
/// <c>FOR UPDATE SKIP LOCKED</c>. Consumers must therefore be idempotent (the message id is the dedup key).
/// </summary>
internal sealed class OutboxDispatcher(IServiceScopeFactory scopes, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DispatchBatchAsync(stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch cycle failed; retrying");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    internal async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.OutboxMessages
            .FromSqlInterpolated($"SELECT * FROM outbox_messages WHERE processed_at IS NULL ORDER BY created_at LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(message.Id, message.Type, message.Payload, ct);
                message.MarkProcessed(clock.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkFailed(ex.Message);
                logger.LogWarning(ex, "Publishing outbox message {MessageId} failed (attempt {Attempts})", message.Id, message.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return batch.Count;
    }
}
