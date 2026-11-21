using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Application.Features.Payments;
using WalletFlow.Domain.Accounts;
using WalletFlow.Domain.Transactions;

namespace WalletFlow.Application.Tests;

public class TopUpHandlerTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly InMemoryStore _store = new();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly Guid _me = Guid.NewGuid();

    private Account NewAccount()
    {
        var account = Account.Open(_me, "TRY");
        _store.Accounts.Add(account);
        return account;
    }

    private TopUpHandler Handler() => new(_store, _store, _gateway, new TestUser(_me), _store, _store, _store, Clock);

    [Fact]
    public async Task TopUp_WhenApproved_CreditsAccountOnce()
    {
        var account = NewAccount();
        _gateway.ChargeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>()).Returns(new PaymentResult(true, "ch_1", null));

        var result = await Handler().Handle(new TopUpCommand(account.Id, 75m, "tok_visa", "key-12345678"), default);

        result.Status.Should().Be("Completed");
        account.Balance.Should().Be(75m);
        _store.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task TopUp_WhenDeclined_FailsTransactionAndKeepsBalance()
    {
        var account = NewAccount();
        _gateway.ChargeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>()).Returns(new PaymentResult(false, null, "card_declined"));

        var act = () => Handler().Handle(new TopUpCommand(account.Id, 75m, "tok_declined", "key-12345678"), default);

        await act.Should().ThrowAsync<PaymentDeclinedException>();
        account.Balance.Should().Be(0m);
        _store.Transactions.Should().ContainSingle().Which.Status.Should().Be(TransactionStatus.Failed);
        _store.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task TopUp_WhenProviderIsDown_FailsTransactionAndKeepsBalance()
    {
        var account = NewAccount();
        _gateway.ChargeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ExternalServiceException("down"));

        var act = () => Handler().Handle(new TopUpCommand(account.Id, 75m, "tok_visa", "key-12345678"), default);

        await act.Should().ThrowAsync<ExternalServiceException>();
        account.Balance.Should().Be(0m);
        _store.Transactions.Single().Status.Should().Be(TransactionStatus.Failed);
    }

    [Fact]
    public async Task TopUp_OnSomeoneElsesAccount_IsNotFoundAndNeverCallsProvider()
    {
        var foreign = Account.Open(Guid.NewGuid(), "TRY");
        _store.Accounts.Add(foreign);

        var act = () => Handler().Handle(new TopUpCommand(foreign.Id, 10m, "tok_visa", "key-12345678"), default);

        await act.Should().ThrowAsync<NotFoundException>();
        await _gateway.DidNotReceiveWithAnyArgs().ChargeAsync(default!, default);
    }

    [Fact]
    public async Task TopUp_ReplayAfterCompletion_DoesNotChargeTheProviderAgain()
    {
        var account = NewAccount();
        _gateway.ChargeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>()).Returns(new PaymentResult(true, "ch_1", null));
        var command = new TopUpCommand(account.Id, 75m, "tok_visa", "key-12345678");

        var first = await Handler().Handle(command, default);
        var replay = await Handler().Handle(command, default);

        replay.Id.Should().Be(first.Id);
        account.Balance.Should().Be(75m);
        await _gateway.Received(1).ChargeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TopUp_RetryAfterProviderOutage_ReusesTheSameProviderIdempotencyKey()
    {
        var account = NewAccount();
        var seen = new List<Guid>();
        _gateway.ChargeAsync(Arg.Do<PaymentRequest>(r => seen.Add(r.PaymentId)), Arg.Any<CancellationToken>())
            .Returns(_ => throw new ExternalServiceException("down"), _ => new PaymentResult(true, "ch_1", null));
        var command = new TopUpCommand(account.Id, 75m, "tok_visa", "key-12345678");

        var outage = () => Handler().Handle(command, default);
        await outage.Should().ThrowAsync<ExternalServiceException>();
        var retry = await Handler().Handle(command, default);

        retry.Status.Should().Be("Completed");
        account.Balance.Should().Be(75m);
        seen.Should().HaveCount(2).And.OnlyContain(id => id == seen[0], "the provider must be able to deduplicate");
        _store.Transactions.Should().ContainSingle();
    }

    [Fact]
    public async Task TopUp_ReplayOfADeclinedKey_ReplaysTheDeclineWithoutCallingTheProvider()
    {
        var account = NewAccount();
        _gateway.ChargeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>()).Returns(new PaymentResult(false, null, "card_declined"));
        var command = new TopUpCommand(account.Id, 75m, "tok_declined", "key-12345678");
        await Handler().Handle(command, default).ContinueWith(_ => { });

        var replay = () => Handler().Handle(command, default);

        await replay.Should().ThrowAsync<PaymentDeclinedException>();
        await _gateway.Received(1).ChargeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>());
    }
}
