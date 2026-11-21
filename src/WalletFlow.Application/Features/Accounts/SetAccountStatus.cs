using FluentValidation;
using MediatR;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Domain.Accounts;

namespace WalletFlow.Application.Features.Accounts;

public sealed record SetAccountStatusCommand(Guid AccountId, AccountStatus Status) : IRequest<AccountDto>;

public sealed class SetAccountStatusCommandValidator : AbstractValidator<SetAccountStatusCommand>
{
    public SetAccountStatusCommandValidator() => RuleFor(x => x.Status).IsInEnum();
}

internal sealed class SetAccountStatusHandler(
    IAccountRepository accounts,
    ICurrentUser user,
    IAuditTrail audit,
    IUnitOfWork uow) : IRequestHandler<SetAccountStatusCommand, AccountDto>
{
    public async Task<AccountDto> Handle(SetAccountStatusCommand request, CancellationToken ct)
    {
        // Defense in depth: the controller policy already restricts this to admins.
        if (!user.IsAdmin)
        {
            throw new ForbiddenException();
        }

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var account = await accounts.GetByIdAsync(request.AccountId, token) ?? throw new NotFoundException("Account not found.");
            if (request.Status == AccountStatus.Frozen)
            {
                account.Freeze();
            }
            else
            {
                account.Activate();
            }

            audit.Record(user.UserId, $"account.{request.Status.ToString().ToLowerInvariant()}", nameof(Account), account.Id.ToString());
            await uow.SaveChangesAsync(token);
            return AccountDto.From(account);
        }, ct);
    }
}
