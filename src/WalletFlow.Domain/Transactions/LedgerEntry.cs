namespace WalletFlow.Domain.Transactions;

/// <summary>One side of a double-entry record. Entries of a transaction always net to zero.</summary>
public sealed class LedgerEntry
{
    private LedgerEntry()
    {
        // Required by EF Core.
        Currency = string.Empty;
    }

    internal LedgerEntry(Guid transactionId, Guid accountId, EntryDirection direction, decimal amount, string currency)
    {
        Id = Guid.NewGuid();
        TransactionId = transactionId;
        AccountId = accountId;
        Direction = direction;
        Amount = amount;
        Currency = currency;
    }

    public Guid Id { get; private set; }

    public Guid TransactionId { get; private set; }

    public Guid AccountId { get; private set; }

    public EntryDirection Direction { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>Signed amount: credits positive, debits negative.</summary>
    public decimal SignedAmount => Direction == EntryDirection.Credit ? Amount : -Amount;
}
