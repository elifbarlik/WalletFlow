namespace WalletFlow.Domain.Auditing;

/// <summary>Append-only record of security- and money-relevant actions.</summary>
public sealed class AuditLog
{
    private AuditLog()
    {
        // Required by EF Core.
        Action = string.Empty;
        EntityType = string.Empty;
    }

    public AuditLog(Guid? userId, string action, string entityType, string? entityId, string? details, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Details = details;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid? UserId { get; private set; }

    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public string? EntityId { get; private set; }

    public string? Details { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
