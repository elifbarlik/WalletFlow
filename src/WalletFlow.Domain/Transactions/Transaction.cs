using WalletFlow.Domain.Accounts;
using WalletFlow.Domain.Common;

namespace WalletFlow.Domain.Transactions;

/// <summary>A money movement together with its ledger entries.</summary>
public sealed class Transaction
{
    /// <summary>Failure reason of a top-up that never reached a verdict; such a top-up may be retried with the same key.</summary>
    public const string ProviderUnavailable = "provider unavailable";

    private readonly List<LedgerEntry> _entries = [];

    private Transaction()
    {
        // Required by EF Core.
        Currency = string.Empty;
        Reference = string.Empty;
    }

    private Transaction(
        Guid id, TransactionType type, Guid sourceAccountId, Guid destinationAccountId, Money amount, string reference, DateTimeOffset now)
    {
        Id = id;
        Type = type;
        Status = TransactionStatus.Pending;
        SourceAccountId = sourceAccountId;
        DestinationAccountId = destinationAccountId;
        Amount = amount.Amount;
        Currency = amount.Currency;
        Reference = reference;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public TransactionType Type { get; private set; }

    public TransactionStatus Status { get; private set; }

    /// <summary>Account that is debited (transfer) or the account being topped up.</summary>
    public Guid SourceAccountId { get; private set; }

    /// <summary>Account that is credited. Equals <see cref="SourceAccountId"/> for top-ups.</summary>
    public Guid DestinationAccountId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public string Reference { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<LedgerEntry> Entries => _entries;

    /// <summary>Moves money between two accounts in memory; persistence wraps it in one DB transaction.</summary>
    /// <param name="id">Optional deterministic id derived from the idempotency key; the primary key then guarantees at-most-once.</param>
    public static Transaction Transfer(Account from, Account to, Money amount, string reference, DateTimeOffset now, Guid? id = null)
    {
        if (from.Id == to.Id)
        {
            throw new DomainException("transfer.same_account", "Cannot transfer to the same account.");
        }

        if (from.Currency != to.Currency)
        {
            throw new DomainException("money.currency_mismatch", "Accounts must share the same currency.");
        }

        from.Debit(amount);
        to.Credit(amount);

        var transaction = new Transaction(id ?? Guid.NewGuid(), TransactionType.Transfer, from.Id, to.Id, amount, reference, now);
        transaction._entries.Add(new LedgerEntry(transaction.Id, from.Id, EntryDirection.Debit, amount.Amount, amount.Currency));
        transaction._entries.Add(new LedgerEntry(transaction.Id, to.Id, EntryDirection.Credit, amount.Amount, amount.Currency));
        transaction.Status = TransactionStatus.Completed;
        return transaction;
    }

    /// <summary>Creates a pending top-up; call <see cref="CompleteTopUp"/> once the payment provider confirms.</summary>
    public static Transaction StartTopUp(Account account, Money amount, string reference, DateTimeOffset now, Guid? id = null)
    {
        if (account.Status == AccountStatus.Frozen)
        {
            throw new DomainException("account.frozen", "Account is frozen.");
        }

        if (!amount.IsPositive || amount.Currency != account.Currency)
        {
            throw new DomainException("money.invalid", "Amount must be positive and in the account currency.");
        }

        return new Transaction(id ?? Guid.NewGuid(), TransactionType.TopUp, account.Id, account.Id, amount, reference, now);
    }

    public void CompleteTopUp(Account account)
    {
        if (Type != TransactionType.TopUp || Status != TransactionStatus.Pending || account.Id != DestinationAccountId)
        {
            throw new DomainException("transaction.invalid_state", "Only a pending top-up of its own account can be completed.");
        }

        account.Credit(new Money(Amount, Currency));
        _entries.Add(new LedgerEntry(Id, account.Id, EntryDirection.Credit, Amount, Currency));
        Status = TransactionStatus.Completed;
    }

    /// <summary>Puts a top-up that failed only because the provider was unreachable back to pending so it can be retried.</summary>
    public void ReopenAfterTransientFailure()
    {
        if (Type != TransactionType.TopUp || Status != TransactionStatus.Failed || FailureReason != ProviderUnavailable)
        {
            throw new DomainException("transaction.invalid_state", "Only a top-up that failed because the provider was unavailable can be retried.");
        }

        Status = TransactionStatus.Pending;
        FailureReason = null;
    }

    public void Fail(string reason)
    {
        if (Status != TransactionStatus.Pending)
        {
            throw new DomainException("transaction.invalid_state", "Only a pending transaction can fail.");
        }

        Status = TransactionStatus.Failed;
        FailureReason = reason.Length > 200 ? reason[..200] : reason;
    }
}

