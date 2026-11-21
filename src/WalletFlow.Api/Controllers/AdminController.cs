using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.Common;
using WalletFlow.Application.Features;
using WalletFlow.Application.Features.Accounts;
using WalletFlow.Application.Features.Admin;

namespace WalletFlow.Api.Controllers;

/// <summary>Back-office operations.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin")]
[Produces("application/json")]
public sealed class AdminController(ISender sender) : ControllerBase
{
    /// <summary>Freezes or re-activates an account (Admin only).</summary>
    [HttpPatch("accounts/{id:guid}/status")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<AccountDto> SetStatus(Guid id, SetAccountStatusRequest request, CancellationToken ct) =>
        await sender.Send(new SetAccountStatusCommand(id, request.Status), ct);

    /// <summary>Reads the audit trail (Admin or Auditor).</summary>
    [HttpGet("audit-logs")]
    [Authorize(Policy = AuthorizationPolicies.AuditorRead)]
    public async Task<PagedResult<AuditLogDto>> AuditLogs(int page = 1, int size = 50, CancellationToken ct = default) =>
        await sender.Send(new GetAuditLogsQuery(page, size), ct);
}

public static class AuthorizationPolicies
{
    public const string AdminOnly = nameof(AdminOnly);
    public const string AuditorRead = nameof(AuditorRead);
}
