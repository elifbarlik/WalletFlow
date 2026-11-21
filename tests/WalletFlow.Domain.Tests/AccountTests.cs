using FluentAssertions;
using WalletFlow.Domain.Accounts;
using WalletFlow.Domain.Common;

namespace WalletFlow.Domain.Tests;

public class AccountTests
{
    private static Account NewAccount(decimal balance = 0m)
    {
        var account = Account.Open(Guid.NewGuid(), "TRY");
        if (balance > 0)
        {
            account.Credit(new Money(balance, "TRY"));
        }

        return account;
    }

    [Fact]
    public void Credit_IncreasesBalance()
    {
        var account = NewAccount(100m);

        account.Credit(new Money(25m, "TRY"));

        account.Balance.Should().Be(125m);
    }

    [Fact]
    public void Debit_WithInsufficientFunds_ThrowsAndKeepsBalance()
    {
        var account = NewAccount(50m);

        var act = () => account.Debit(new Money(50.01m, "TRY"));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("account.insufficient_funds");
        account.Balance.Should().Be(50m);
    }

    [Fact]
    public void Debit_WhenFrozen_Throws()
    {
        var account = NewAccount(50m);
        account.Freeze();

        var act = () => account.Debit(new Money(1m, "TRY"));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("account.frozen");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Credit_WithNonPositiveAmount_Throws(double amount)
    {
        var account = NewAccount();

        var act = () => account.Credit(new Money((decimal)amount, "TRY"));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("money.non_positive");
    }

    [Fact]
    public void Credit_WithOtherCurrency_Throws()
    {
        var account = NewAccount();

        var act = () => account.Credit(new Money(1m, "USD"));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("money.currency_mismatch");
    }
}
