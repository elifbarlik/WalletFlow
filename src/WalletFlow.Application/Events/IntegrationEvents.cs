namespace WalletFlow.Application.Events;

/// <summary>Marker for events published through the transactional outbox.</summary>
public interface IIntegrationEvent
{
    Guid TransactionId { get; }

    DateTimeOffset OccurredAt { get; }
}

public sealed record TransferCompleted(
    Guid TransactionId,
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

public sealed record TopUpCompleted(
    Guid TransactionId,
    Guid AccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
