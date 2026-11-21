using WalletFlow.Domain.Common;

namespace WalletFlow.Domain.Accounts;

/// <summary>A customer wallet. Balance changes only through <see cref="Credit"/> and <see cref="Debit"/>.</summary>
public sealed class Account
{
    private Account()
    {
        // Required by EF Core.
        Currency = string.Empty;
    }

    private Account(Guid id, Guid ownerId, string currency)
    {
        Id = id;
        OwnerId = ownerId;
        Currency = currency;
        Balance = 0m;
        Status = AccountStatus.Active;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Currency { get; private set; }

    public decimal Balance { get; private set; }

    public AccountStatus Status { get; private set; }

    /// <summary>Optimistic concurrency token (mapped to PostgreSQL xmin in Infrastructure).</summary>
    public uint Version { get; private set; }

    public Money CurrentBalance => new(Balance, Currency);

    public static Account Open(Guid ownerId, string currency)
    {
        if (ownerId == Guid.Empty)
        {
            throw new DomainException("account.invalid_owner", "Owner is required.");
        }

        return new Account(Guid.NewGuid(), ownerId, new Money(0m, currency).Currency);
    }

    public void Credit(Money amount)
    {
        EnsureUsable(amount);
        Balance = (CurrentBalance + amount).Amount;
    }

    public void Debit(Money amount)
    {
        EnsureUsable(amount);
        if (CurrentBalance < amount)
        {
            throw new DomainException("account.insufficient_funds", "Insufficient funds.");
        }

        Balance = (CurrentBalance - amount).Amount;
    }

    public void Freeze() => Status = AccountStatus.Frozen;

    public void Activate() => Status = AccountStatus.Active;

    private void EnsureUsable(Money amount)
    {
        if (Status == AccountStatus.Frozen)
        {
            throw new DomainException("account.frozen", "Account is frozen.");
        }

        if (!amount.IsPositive)
        {
            throw new DomainException("money.non_positive", "Amount must be greater than zero.");
        }

        if (amount.Currency != Currency)
        {
            throw new DomainException("money.currency_mismatch", $"Account currency is {Currency}, got {amount.Currency}.");
        }
    }
}
