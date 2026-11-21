using WalletFlow.Domain.Common;

namespace WalletFlow.Domain.Idempotency;

/// <summary>Remembers the outcome of a request identified by (user, Idempotency-Key).</summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
        // Required by EF Core.
        Key = string.Empty;
        RequestHash = string.Empty;
        RequestName = string.Empty;
    }

    public IdempotencyRecord(Guid userId, string key, string requestName, string requestHash, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Key = key;
        RequestName = requestName;
        RequestHash = requestHash;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Key { get; private set; }

    public string RequestName { get; private set; }

    public string RequestHash { get; private set; }

    public bool IsCompleted { get; private set; }

    public string? ResponseJson { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Complete(string responseJson)
    {
        if (IsCompleted)
        {
            throw new DomainException("idempotency.already_completed", "Record is already completed.");
        }

        IsCompleted = true;
        ResponseJson = responseJson;
    }
}
