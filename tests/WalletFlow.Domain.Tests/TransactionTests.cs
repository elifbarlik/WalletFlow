using FluentAssertions;
using WalletFlow.Domain.Accounts;
using WalletFlow.Domain.Common;
using WalletFlow.Domain.Transactions;

namespace WalletFlow.Domain.Tests;

public class TransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Account Funded(decimal balance)
    {
        var account = Account.Open(Guid.NewGuid(), "TRY");
        if (balance > 0)
        {
            account.Credit(new Money(balance, "TRY"));
        }

        return account;
    }

    [Fact]
    public void Transfer_MovesMoney_AndLedgerNetsToZero()
    {
        var from = Funded(100m);
        var to = Funded(10m);

        var tx = Transaction.Transfer(from, to, new Money(40m, "TRY"), "rent", Now);

        from.Balance.Should().Be(60m);
        to.Balance.Should().Be(50m);
        tx.Status.Should().Be(TransactionStatus.Completed);
        tx.Entries.Should().HaveCount(2);
        tx.Entries.Sum(e => e.SignedAmount).Should().Be(0m);
    }

    [Fact]
    public void Transfer_ToSameAccount_Throws()
    {
        var account = Funded(100m);

        var act = () => Transaction.Transfer(account, account, new Money(1m, "TRY"), "x", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("transfer.same_account");
    }

    [Fact]
    public void Transfer_WithInsufficientFunds_DoesNotChangeReceiver()
    {
        var from = Funded(10m);
        var to = Funded(0m);

        var act = () => Transaction.Transfer(from, to, new Money(11m, "TRY"), "x", Now);

        act.Should().Throw<DomainException>();
        to.Balance.Should().Be(0m);
    }

    [Fact]
    public void TopUp_WhenCompleted_CreditsAccount()
    {
        var account = Funded(0.01m);
        var tx = Transaction.StartTopUp(account, new Money(99.99m, "TRY"), "card", Now);

        tx.CompleteTopUp(account);

        account.Balance.Should().Be(100m);
        tx.Status.Should().Be(TransactionStatus.Completed);
    }

    [Fact]
    public void TopUp_WhenFailed_LeavesBalanceUntouched()
    {
        var account = Funded(5m);
        var tx = Transaction.StartTopUp(account, new Money(10m, "TRY"), "card", Now);

        tx.Fail("provider unavailable");

        account.Balance.Should().Be(5m);
        tx.Status.Should().Be(TransactionStatus.Failed);
        tx.FailureReason.Should().Be("provider unavailable");
    }

    [Fact]
    public void TopUp_FailedBecauseProviderWasUnavailable_CanBeReopened()
    {
        var account = Funded(0m);
        var tx = Transaction.StartTopUp(account, new Money(10m, "TRY"), "card", Now);
        tx.Fail(Transaction.ProviderUnavailable);

        tx.ReopenAfterTransientFailure();

        tx.Status.Should().Be(TransactionStatus.Pending);
        tx.FailureReason.Should().BeNull();
    }

    [Fact]
    public void TopUp_DeclinedByTheProvider_CannotBeReopened()
    {
        var account = Funded(0m);
        var tx = Transaction.StartTopUp(account, new Money(10m, "TRY"), "card", Now);
        tx.Fail("card_declined");

        var act = tx.ReopenAfterTransientFailure;

        act.Should().Throw<DomainException>().Which.Code.Should().Be("transaction.invalid_state");
    }

    [Fact]
    public void Transfer_UsesTheSuppliedDeterministicId()
    {
        var id = Guid.NewGuid();

        var tx = Transaction.Transfer(Funded(100m), Funded(0m), new Money(1m, "TRY"), "x", Now, id);

        tx.Id.Should().Be(id);
    }
}
