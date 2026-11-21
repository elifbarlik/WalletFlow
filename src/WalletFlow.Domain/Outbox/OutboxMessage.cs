namespace WalletFlow.Domain.Outbox;

/// <summary>Integration event persisted in the same DB transaction as the state change that produced it.</summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        // Required by EF Core.
        Type = string.Empty;
        Payload = string.Empty;
    }

    public OutboxMessage(string type, string payload, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Type = type;
        Payload = payload;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; }

    public string Payload { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public void MarkProcessed(DateTimeOffset now) => ProcessedAt = now;

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
    }
}
