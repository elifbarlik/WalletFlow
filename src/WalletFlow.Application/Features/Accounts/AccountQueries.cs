using FluentValidation;
using MediatR;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;

namespace WalletFlow.Application.Features.Accounts;

public sealed record GetAccountQuery(Guid AccountId) : IRequest<AccountDto>;

public sealed record ListMyAccountsQuery : IRequest<IReadOnlyList<AccountDto>>;

public sealed record GetAccountTransactionsQuery(Guid AccountId, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<TransactionDto>>;

public sealed class GetAccountTransactionsQueryValidator : AbstractValidator<GetAccountTransactionsQuery>
{
    public GetAccountTransactionsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

internal sealed class GetAccountHandler(IAccountRepository accounts, ICurrentUser user)
    : IRequestHandler<GetAccountQuery, AccountDto>
{
    public async Task<AccountDto> Handle(GetAccountQuery request, CancellationToken ct)
    {
        var account = (await accounts.GetByIdAsync(request.AccountId, ct)).EnsureOwnedBy(user, allowAdmin: true);
        return AccountDto.From(account);
    }
}

internal sealed class ListMyAccountsHandler(IAccountRepository accounts, ICurrentUser user)
    : IRequestHandler<ListMyAccountsQuery, IReadOnlyList<AccountDto>>
{
    public async Task<IReadOnlyList<AccountDto>> Handle(ListMyAccountsQuery request, CancellationToken ct) =>
        (await accounts.ListByOwnerAsync(user.UserId, ct)).Select(AccountDto.From).ToList();
}

internal sealed class GetAccountTransactionsHandler(
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ICurrentUser user) : IRequestHandler<GetAccountTransactionsQuery, PagedResult<TransactionDto>>
{
    public async Task<PagedResult<TransactionDto>> Handle(GetAccountTransactionsQuery request, CancellationToken ct)
    {
        (await accounts.GetByIdAsync(request.AccountId, ct)).EnsureOwnedBy(user, allowAdmin: true);
        var (items, total) = await transactions.GetPageForAccountAsync(request.AccountId, request.Page, request.PageSize, ct);
        return new PagedResult<TransactionDto>(items.Select(TransactionDto.From).ToList(), request.Page, request.PageSize, total);
    }
}
