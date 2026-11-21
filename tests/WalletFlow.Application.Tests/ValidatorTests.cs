using FluentAssertions;
using FluentValidation.TestHelper;
using WalletFlow.Application.Features.Accounts;
using WalletFlow.Application.Features.Auth;
using WalletFlow.Application.Features.Payments;

namespace WalletFlow.Application.Tests;

public class ValidatorTests
{
    [Theory]
    [InlineData("short1A", false)]
    [InlineData("alllowercase1234", false)]
    [InlineData("ALLUPPERCASE1234", false)]
    [InlineData("NoDigitsHereAtAll", false)]
    [InlineData("GoodPassword123", true)]
    public void Register_EnforcesPasswordPolicy(string password, bool valid)
    {
        var result = new RegisterCommandValidator().TestValidate(new RegisterCommand("a@b.test", password));

        result.IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_000.01)]
    [InlineData(10.001)]
    public void Transfer_RejectsInvalidAmounts(double amount)
    {
        var result = new TransferCommandValidator().TestValidate(
            new TransferCommand(Guid.NewGuid(), Guid.NewGuid(), (decimal)amount, null, "key-12345678"));

        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has spaces in the key!")]
    public void Transfer_RequiresWellFormedIdempotencyKey(string key)
    {
        var result = new TransferCommandValidator().TestValidate(
            new TransferCommand(Guid.NewGuid(), Guid.NewGuid(), 10m, null, key));

        result.ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }

    [Fact]
    public void Transfer_RejectsSameSourceAndDestination()
    {
        var id = Guid.NewGuid();

        new TransferCommandValidator().TestValidate(new TransferCommand(id, id, 10m, null, "key-12345678"))
            .ShouldHaveValidationErrorFor(x => x.ToAccountId);
    }

    [Fact]
    public void TopUp_RejectsTokensWithUnexpectedCharacters()
    {
        new TopUpCommandValidator().TestValidate(new TopUpCommand(Guid.NewGuid(), 10m, "4111 1111 1111 1111", "key-12345678"))
            .ShouldHaveValidationErrorFor(x => x.SourceToken);
    }

    [Theory]
    [InlineData("TRY", true)]
    [InlineData("usd", true)]
    [InlineData("BTC", false)]
    public void OpenAccount_SupportsOnlyKnownCurrencies(string currency, bool valid) =>
        new OpenAccountCommandValidator().TestValidate(new OpenAccountCommand(currency)).IsValid.Should().Be(valid);
}
