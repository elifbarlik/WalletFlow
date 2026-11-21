using System.Text.Json;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Events;
using WalletFlow.Domain.Auditing;
using WalletFlow.Domain.Outbox;

namespace WalletFlow.Infrastructure.Persistence;

internal sealed class AuditTrail(AppDbContext db, IClock clock) : IAuditTrail
{
    public void Record(Guid? userId, string action, string entityType, string? entityId = null, string? details = null) =>
        db.AuditLogs.Add(new AuditLog(userId, action, entityType, entityId, Truncate(details), clock.UtcNow));

    private static string? Truncate(string? value) => value is { Length: > 500 } ? value[..500] : value;
}

internal sealed class Outbox(AppDbContext db, IClock clock) : IOutbox
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Enqueue<TEvent>(TEvent integrationEvent)
        where TEvent : IIntegrationEvent =>
        db.OutboxMessages.Add(new OutboxMessage(typeof(TEvent).Name, JsonSerializer.Serialize(integrationEvent, Json), clock.UtcNow));
}

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
