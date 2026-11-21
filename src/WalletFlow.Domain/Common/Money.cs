namespace WalletFlow.Domain.Common;

/// <summary>Immutable monetary amount. Uses <see cref="decimal"/> and never floating point.</summary>
public readonly record struct Money
{
    public const int Scale = 2;

    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new DomainException("money.invalid_currency", "Currency must be a 3-letter ISO code.");
        }

        Amount = decimal.Round(amount, Scale, MidpointRounding.ToEven);
        Currency = currency.ToUpperInvariant();
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero(string currency) => new(0m, currency);

    public bool IsPositive => Amount > 0;

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount < right.Amount;
    }

    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount > right.Amount;
    }

    public static bool operator <=(Money left, Money right) => !(left > right);

    public static bool operator >=(Money left, Money right) => !(left < right);

    public override string ToString() => $"{Amount:0.00} {Currency}";

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new DomainException("money.currency_mismatch", $"Cannot combine {left.Currency} with {right.Currency}.");
        }
    }
}
