using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Infrastructure;
using WalletFlow.Application.Common;
using WalletFlow.Application.Features;
using WalletFlow.Application.Features.Accounts;
using WalletFlow.Application.Features.Payments;

namespace WalletFlow.Api.Controllers;

/// <summary>Wallet accounts owned by the caller.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/accounts")]
[Produces("application/json")]
public sealed class AccountsController(ISender sender) : ControllerBase
{
    private const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>Opens a new wallet in TRY, USD or EUR (max 5 per user).</summary>
    [HttpPost]
    [ProducesResponseType<AccountDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Open(OpenAccountRequest request, CancellationToken ct)
    {
        var account = await sender.Send(new OpenAccountCommand(request.Currency), ct);
        return CreatedAtAction(nameof(Get), new { id = account.Id, version = "1.0" }, account);
    }

    /// <summary>Lists the caller's accounts.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<AccountDto>> List(CancellationToken ct) => await sender.Send(new ListMyAccountsQuery(), ct);

    /// <summary>Gets one account. Accounts of other users are reported as 404.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AccountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AccountDto> Get(Guid id, CancellationToken ct) => await sender.Send(new GetAccountQuery(id), ct);

    /// <summary>Paged transaction history, newest first.</summary>
    [HttpGet("{id:guid}/transactions")]
    public async Task<PagedResult<TransactionDto>> Transactions(Guid id, int page = 1, int size = 20, CancellationToken ct = default) =>
        await sender.Send(new GetAccountTransactionsQuery(id, page, size), ct);

    /// <summary>
    /// Adds money through the external payment provider. Requires an <c>Idempotency-Key</c> header; retrying with the
    /// same key and body never charges twice. Use token <c>tok_declined</c> to simulate a decline.
    /// </summary>
    [HttpPost("{id:guid}/topups")]
    [EnableRateLimiting(RateLimiting.Money)]
    [ProducesResponseType<TransactionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> TopUp(
        Guid id, TopUpRequest request, [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created,
            await sender.Send(new TopUpCommand(id, request.Amount, request.SourceToken, idempotencyKey ?? string.Empty), ct));
}
