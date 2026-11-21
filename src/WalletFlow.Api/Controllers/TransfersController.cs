using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Infrastructure;
using WalletFlow.Application.Features;
using WalletFlow.Application.Features.Payments;

namespace WalletFlow.Api.Controllers;

/// <summary>Wallet-to-wallet transfers.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/transfers")]
[EnableRateLimiting(RateLimiting.Money)]
[Produces("application/json")]
public sealed class TransfersController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Moves money atomically (double-entry ledger). Requires an <c>Idempotency-Key</c> header; replaying the same
    /// request returns the original result without moving money again.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<TransactionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Transfer(
        TransferRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created,
            await sender.Send(new TransferCommand(
                request.FromAccountId, request.ToAccountId, request.Amount, request.Reference, idempotencyKey ?? string.Empty), ct));
}
