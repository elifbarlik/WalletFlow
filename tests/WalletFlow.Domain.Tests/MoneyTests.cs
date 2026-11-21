using FluentAssertions;
using WalletFlow.Domain.Common;

namespace WalletFlow.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Addition_WithSameCurrency_SumsAmounts()
    {
        var result = new Money(10.50m, "TRY") + new Money(2.25m, "try");

        result.Should().Be(new Money(12.75m, "TRY"));
    }

    [Fact]
    public void Addition_WithDifferentCurrency_Throws()
    {
        var act = () => new Money(1m, "TRY") + new Money(1m, "USD");

        act.Should().Throw<DomainException>().Which.Code.Should().Be("money.currency_mismatch");
    }

    [Fact]
    public void Constructor_RoundsToTwoDecimals_UsingBankersRounding()
    {
        new Money(1.005m, "TRY").Amount.Should().Be(1.00m);
        new Money(1.015m, "TRY").Amount.Should().Be(1.02m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TL")]
    [InlineData("TURK")]
    public void Constructor_WithInvalidCurrency_Throws(string currency)
    {
        var act = () => new Money(1m, currency);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("money.invalid_currency");
    }
}
