using FluentValidation;
using MediatR;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Domain.Users;

namespace WalletFlow.Application.Features.Admin;

public sealed record GetAuditLogsQuery(int Page = 1, int PageSize = 50) : IRequest<PagedResult<AuditLogDto>>;

public sealed class GetAuditLogsQueryValidator : AbstractValidator<GetAuditLogsQuery>
{
    public GetAuditLogsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

internal sealed class GetAuditLogsHandler(IAuditLogRepository logs, ICurrentUser user)
    : IRequestHandler<GetAuditLogsQuery, PagedResult<AuditLogDto>>
{
    public async Task<PagedResult<AuditLogDto>> Handle(GetAuditLogsQuery request, CancellationToken ct)
    {
        if (user.Role is not (Role.Admin or Role.Auditor))
        {
            throw new ForbiddenException();
        }

        var (items, total) = await logs.GetPageAsync(request.Page, request.PageSize, ct);
        return new PagedResult<AuditLogDto>(items.Select(AuditLogDto.From).ToList(), request.Page, request.PageSize, total);
    }
}
