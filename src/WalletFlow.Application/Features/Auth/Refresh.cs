using FluentValidation;
using MediatR;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Domain.Users;

namespace WalletFlow.Application.Features.Auth;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthTokensDto>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(256);
}

/// <summary>Rotates refresh tokens. Presenting an already-used token revokes the whole token family (theft signal).</summary>
internal sealed class RefreshTokenHandler(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    ITokenService tokens,
    IAuditTrail audit,
    IUnitOfWork uow,
    IClock clock) : IRequestHandler<RefreshTokenCommand, AuthTokensDto>
{
    public async Task<AuthTokensDto> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var stored = await refreshTokens.GetByHashAsync(hash, ct) ?? throw new AuthenticationFailedException();

        if (!await refreshTokens.TryRevokeAsync(hash, now, ct))
        {
            if (stored.RevokedAt is not null)
            {
                await refreshTokens.RevokeAllAsync(stored.UserId, now, ct);
                audit.Record(stored.UserId, "auth.refresh_token_reuse_detected", nameof(RefreshToken), stored.Id.ToString());
                await uow.SaveChangesAsync(ct);
            }

            throw new AuthenticationFailedException();
        }

        var user = await users.GetByIdAsync(stored.UserId, ct) ?? throw new AuthenticationFailedException();
        var next = tokens.GenerateRefreshToken();
        refreshTokens.Add(new RefreshToken(user.Id, next.Hash, now, tokens.RefreshTokenLifetime));
        audit.Record(user.Id, "auth.token_refreshed", nameof(User), user.Id.ToString());
        await uow.SaveChangesAsync(ct);

        var access = tokens.CreateAccessToken(user);
        return new AuthTokensDto(access.Value, access.ExpiresAt, next.Raw);
    }
}
