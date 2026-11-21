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

public sealed record TransferCommand(
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string? Reference,
    [property: JsonIgnore] string IdempotencyKey) : IRequest<TransactionDto>, IIdempotentRequest;

public sealed class TransferCommandValidator : AbstractValidator<TransferCommand>
{
    public TransferCommandValidator()
    {
        RuleFor(x => x.FromAccountId).NotEmpty();
        RuleFor(x => x.ToAccountId).NotEmpty().NotEqual(x => x.FromAccountId).WithMessage("Source and destination must differ.");
        RuleFor(x => x.Amount).MoneyAmount();
        RuleFor(x => x.Reference).MaximumLength(140);
        RuleFor(x => x.IdempotencyKey).IdempotencyKey();
    }
}

/// <summary>
/// The transaction id is derived from the Idempotency-Key, so even when the key store has forgotten the request
/// (e.g. a Redis flush) the primary key makes a second execution impossible: the replay returns the original result.
/// </summary>
internal sealed class TransferHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ICurrentUser user,
    IOutbox outbox,
    IAuditTrail audit,
    IUnitOfWork uow,
    IClock clock) : IRequestHandler<TransferCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(TransferCommand request, CancellationToken ct)
    {
        var id = IdempotentIds.For(user.UserId, "transfer", request.IdempotencyKey);
        try
        {
            return await uow.ExecuteInTransactionAsync(token => ExecuteAsync(request, id, token), ct);
        }
        catch (DuplicateKeyException)
        {
            // Lost a race with an identical request that committed first.
            return await ReplayAsync(request, id, ct) ?? throw new RequestInProgressException();
        }
    }

    private async Task<TransactionDto> ExecuteAsync(TransferCommand request, Guid id, CancellationToken ct)
    {
        var loaded = await accounts.GetByIdsAsync([request.FromAccountId, request.ToAccountId], ct);
        var from = loaded.FirstOrDefault(a => a.Id == request.FromAccountId).EnsureOwnedBy(user);

        if (await ReplayAsync(request, id, ct) is { } replay)
        {
            return replay;
        }

        var to = loaded.FirstOrDefault(a => a.Id == request.ToAccountId)
            ?? throw new NotFoundException("Destination account not found.");

        var now = clock.UtcNow;
        var transaction = Transaction.Transfer(from, to, new Money(request.Amount, from.Currency), request.Reference ?? string.Empty, now, id);
        transactions.Add(transaction);
        outbox.Enqueue(new TransferCompleted(transaction.Id, from.Id, to.Id, transaction.Amount, transaction.Currency, now));
        audit.Record(user.UserId, "transfer.completed", nameof(Transaction), transaction.Id.ToString());
        await uow.SaveChangesAsync(ct);
        return TransactionDto.From(transaction);
    }

    private async Task<TransactionDto?> ReplayAsync(TransferCommand request, Guid id, CancellationToken ct)
    {
        var existing = await transactions.GetByIdAsync(id, ct);
        if (existing is null)
        {
            return null;
        }

        var sameRequest = existing.SourceAccountId == request.FromAccountId
            && existing.DestinationAccountId == request.ToAccountId
            && existing.Amount == new Money(request.Amount, existing.Currency).Amount;
        return sameRequest ? TransactionDto.From(existing) : throw new IdempotencyKeyReusedException();
    }
}
