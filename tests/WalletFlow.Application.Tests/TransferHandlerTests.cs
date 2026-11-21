using FluentAssertions;
using WalletFlow.Application.Common;
using WalletFlow.Application.Events;
using WalletFlow.Application.Features.Payments;
using WalletFlow.Domain.Accounts;
using WalletFlow.Domain.Common;

namespace WalletFlow.Application.Tests;

public class TransferHandlerTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly InMemoryStore _store = new();
    private readonly Guid _me = Guid.NewGuid();

    private Account Fund(Guid owner, decimal balance)
    {
        var account = Account.Open(owner, "TRY");
        if (balance > 0)
        {
            account.Credit(new Money(balance, "TRY"));
        }

        _store.Accounts.Add(account);
        return account;
    }

    private TransferHandler Handler(Guid? userId = null) =>
        new(_store, _store, new TestUser(userId ?? _me), _store, _store, _store, Clock);

    [Fact]
    public async Task Transfer_MovesMoney_AndStagesOutboxEventAndAudit()
    {
        var from = Fund(_me, 100m);
        var to = Fund(Guid.NewGuid(), 0m);

        var result = await Handler().Handle(new TransferCommand(from.Id, to.Id, 40m, "rent", "key-12345678"), default);

        result.Status.Should().Be("Completed");
        from.Balance.Should().Be(60m);
        to.Balance.Should().Be(40m);
        _store.Events.Should().ContainSingle().Which.Should().BeOfType<TransferCompleted>();
        _store.AuditActions.Should().Contain("transfer.completed");
    }

    [Fact]
    public async Task Transfer_FromSomeoneElsesAccount_IsReportedAsNotFound()
    {
        var foreign = Fund(Guid.NewGuid(), 100m);
        var to = Fund(_me, 0m);

        var act = () => Handler().Handle(new TransferCommand(foreign.Id, to.Id, 10m, null, "key-12345678"), default);

        await act.Should().ThrowAsync<NotFoundException>();
        foreign.Balance.Should().Be(100m);
    }

    [Fact]
    public async Task Transfer_ToUnknownAccount_IsNotFound()
    {
        var from = Fund(_me, 100m);

        var act = () => Handler().Handle(new TransferCommand(from.Id, Guid.NewGuid(), 10m, null, "key-12345678"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Transfer_WithInsufficientFunds_ThrowsAndStagesNothing()
    {
        var from = Fund(_me, 5m);
        var to = Fund(Guid.NewGuid(), 0m);

        var act = () => Handler().Handle(new TransferCommand(from.Id, to.Id, 10m, null, "key-12345678"), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("account.insufficient_funds");
        _store.Transactions.Should().BeEmpty();
        _store.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Transfer_ReplayedAfterTheKeyStoreForgot_ReturnsTheOriginalWithoutMovingMoneyAgain()
    {
        var from = Fund(_me, 100m);
        var to = Fund(Guid.NewGuid(), 0m);
        var command = new TransferCommand(from.Id, to.Id, 40m, "rent", "key-12345678");

        var first = await Handler().Handle(command, default);
        var replay = await Handler().Handle(command, default); // no idempotency store involved: the id alone protects

        replay.Id.Should().Be(first.Id);
        from.Balance.Should().Be(60m);
        to.Balance.Should().Be(40m);
        _store.Transactions.Should().ContainSingle();
        _store.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task Transfer_SameKeyDifferentAmount_IsRejectedEvenWithoutTheKeyStore()
    {
        var from = Fund(_me, 100m);
        var to = Fund(Guid.NewGuid(), 0m);
        await Handler().Handle(new TransferCommand(from.Id, to.Id, 40m, null, "key-12345678"), default);

        var act = () => Handler().Handle(new TransferCommand(from.Id, to.Id, 41m, null, "key-12345678"), default);

        await act.Should().ThrowAsync<IdempotencyKeyReusedException>();
        from.Balance.Should().Be(60m);
    }
}
