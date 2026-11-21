using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Application.Events;
using WalletFlow.Application.Features.Accounts;
using WalletFlow.Domain.Common;
using WalletFlow.Domain.Transactions;

namespace WalletFlow.Application.Features.Payments;

/// <summary>Adds money to an account through the external payment provider.</summary>
/// <param name="SourceToken">Opaque, provider-issued payment-method token. Card numbers are never handled here.</param>
public sealed record TopUpCommand(
    Guid AccountId,
    decimal Amount,
    string SourceToken,
    [property: JsonIgnore] string IdempotencyKey) : IRequest<TransactionDto>, IIdempotentRequest;

public sealed class TopUpCommandValidator : AbstractValidator<TopUpCommand>
{
    public TopUpCommandValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Amount).MoneyAmount();
        RuleFor(x => x.SourceToken).NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9_]+$");
        RuleFor(x => x.IdempotencyKey).IdempotencyKey();
    }
}

/// <summary>
/// Three short steps so no DB transaction stays open during the HTTP call:
/// 1) persist a Pending transaction, 2) charge the provider, 3) atomically credit (or persist the failure).
/// The transaction id is derived from the Idempotency-Key and is also the provider's idempotency key, so a retry
/// can never create a second charge, even if the key store forgot the request.
/// </summary>
internal sealed class TopUpHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    IPaymentGateway gateway,
    ICurrentUser user,
    IOutbox outbox,
    IAuditTrail audit,
    IUnitOfWork uow,
    IClock clock) : IRequestHandler<TopUpCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(TopUpCommand request, CancellationToken ct)
    {
        var id = IdempotentIds.For(user.UserId, "topup", request.IdempotencyKey);

        TransactionDto pending;
        try
        {
            pending = await uow.ExecuteInTransactionAsync(token => StartOrResumeAsync(request, id, token), ct);
        }
        catch (DuplicateKeyException)
        {
            throw new RequestInProgressException();
        }

        if (pending.Status == nameof(TransactionStatus.Completed))
        {
            return pending;
        }

        PaymentResult result;
        try
        {
            result = await gateway.ChargeAsync(
                new PaymentRequest(pending.Id, pending.Amount, pending.Currency, request.SourceToken, "Wallet top-up"), ct);
        }
        catch (ExternalServiceException ex)
        {
            await FailAsync(pending.Id, Transaction.ProviderUnavailable, ct);
            throw new ExternalServiceException(ex.Message);
        }

        if (!result.Approved)
        {
            var reason = result.DeclineReason ?? "declined";
            await FailAsync(pending.Id, reason, ct);
            throw new PaymentDeclinedException(reason);
        }

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var tx = await transactions.GetByIdAsync(pending.Id, token) ?? throw new NotFoundException("Transaction not found.");
            if (tx.Status == TransactionStatus.Completed)
            {
                return TransactionDto.From(tx); // a concurrent duplicate already credited the account
            }

            var account = await accounts.GetByIdAsync(tx.DestinationAccountId, token) ?? throw new NotFoundException("Account not found.");
            tx.CompleteTopUp(account);
            outbox.Enqueue(new TopUpCompleted(tx.Id, account.Id, tx.Amount, tx.Currency, clock.UtcNow));
            audit.Record(user.UserId, "topup.completed", nameof(Transaction), tx.Id.ToString(), $"provider_ref={result.ProviderReference}");
            await uow.SaveChangesAsync(token);
            return TransactionDto.From(tx);
        }, ct);
    }

    private async Task<TransactionDto> StartOrResumeAsync(TopUpCommand request, Guid id, CancellationToken ct)
    {
        var account = (await accounts.GetByIdAsync(request.AccountId, ct)).EnsureOwnedBy(user);
        var existing = await transactions.GetByIdAsync(id, ct);
        if (existing is null)
        {
            var tx = Transaction.StartTopUp(account, new Money(request.Amount, account.Currency), "card top-up", clock.UtcNow, id);
            transactions.Add(tx);
            await uow.SaveChangesAsync(ct);
            return TransactionDto.From(tx);
        }

        if (existing.SourceAccountId != account.Id || existing.Amount != new Money(request.Amount, account.Currency).Amount)
        {
            throw new IdempotencyKeyReusedException();
        }

        switch (existing.Status)
        {
            case TransactionStatus.Failed when existing.FailureReason == Transaction.ProviderUnavailable:
                // The provider never gave a verdict: retry with the same provider idempotency key (no double charge).
                existing.ReopenAfterTransientFailure();
                await uow.SaveChangesAsync(ct);
                break;
            case TransactionStatus.Failed:
                throw new PaymentDeclinedException(existing.FailureReason ?? "declined");
        }

        return TransactionDto.From(existing);
    }

    private Task FailAsync(Guid transactionId, string reason, CancellationToken ct) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var tx = await transactions.GetByIdAsync(transactionId, token) ?? throw new NotFoundException("Transaction not found.");
            if (tx.Status == TransactionStatus.Pending)
            {
                tx.Fail(reason);
                audit.Record(user.UserId, "topup.failed", nameof(Transaction), tx.Id.ToString(), reason);
                await uow.SaveChangesAsync(token);
            }

            return true;
        }, ct);
}
