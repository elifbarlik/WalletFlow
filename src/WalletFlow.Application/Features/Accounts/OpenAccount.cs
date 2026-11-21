using FluentValidation;
using MediatR;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Domain.Accounts;

namespace WalletFlow.Application.Features.Accounts;

public sealed record OpenAccountCommand(string Currency) : IRequest<AccountDto>;

public sealed class OpenAccountCommandValidator : AbstractValidator<OpenAccountCommand>
{
    public static readonly string[] SupportedCurrencies = ["TRY", "USD", "EUR"];

    public OpenAccountCommandValidator() =>
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Must(c => SupportedCurrencies.Contains(c?.ToUpperInvariant()))
            .WithMessage($"Currency must be one of: {string.Join(", ", SupportedCurrencies)}.");
}

internal sealed class OpenAccountHandler(
    IAccountRepository accounts,
    ICurrentUser user,
    IAuditTrail audit,
    IUnitOfWork uow) : IRequestHandler<OpenAccountCommand, AccountDto>
{
    public const int MaxAccountsPerUser = 5;

    public async Task<AccountDto> Handle(OpenAccountCommand request, CancellationToken ct)
    {
        if (await accounts.CountByOwnerAsync(user.UserId, ct) >= MaxAccountsPerUser)
        {
            throw new ConflictException("account.limit_reached", $"You can hold at most {MaxAccountsPerUser} accounts.");
        }

        var account = Account.Open(user.UserId, request.Currency);
        accounts.Add(account);
        audit.Record(user.UserId, "account.opened", nameof(Account), account.Id.ToString());
        await uow.SaveChangesAsync(ct);
        return AccountDto.From(account);
    }
}
