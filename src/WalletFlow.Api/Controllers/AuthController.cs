using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Infrastructure;
using WalletFlow.Application.Features;
using WalletFlow.Application.Features.Auth;

namespace WalletFlow.Api.Controllers;

/// <summary>Registration, login and token refresh.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimiting.Auth)]
[Produces("application/json")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    /// <summary>Creates a customer account. Passwords need 12+ characters with upper, lower and digit.</summary>
    [HttpPost("register")]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await sender.Send(new RegisterCommand(request.Email, request.Password), ct));

    /// <summary>Exchanges credentials for a short-lived access token and a rotating refresh token.</summary>
    [HttpPost("login")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<AuthTokensDto> Login(LoginRequest request, CancellationToken ct) =>
        await sender.Send(new LoginCommand(request.Email, request.Password), ct);

    /// <summary>Rotates the refresh token. Re-using an old token revokes every session of that user.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<AuthTokensDto> Refresh(RefreshRequest request, CancellationToken ct) =>
        await sender.Send(new RefreshTokenCommand(request.RefreshToken), ct);
}
