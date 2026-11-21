using FluentValidation;
using MediatR;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Domain.Users;

namespace WalletFlow.Application.Features.Auth;

public sealed record LoginCommand(string Email, string Password) : IRequest<AuthTokensDto>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

internal sealed class LoginHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    ITokenService tokens,
    IAuditTrail audit,
    IUnitOfWork uow,
    IClock clock) : IRequestHandler<LoginCommand, AuthTokensDto>
{
    public async Task<AuthTokensDto> Handle(LoginCommand request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var user = await users.GetByEmailAsync(User.NormalizeEmail(request.Email), ct);
        if (user is null)
        {
            hasher.SimulateVerification(request.Password);
            throw new AuthenticationFailedException();
        }

        // Locked accounts get the same generic answer, and the password is not even checked.
        if (user.IsLockedOut(now))
        {
            audit.Record(user.Id, "auth.login_blocked_locked_out", nameof(User), user.Id.ToString());
            await uow.SaveChangesAsync(ct);
            throw new AuthenticationFailedException();
        }

        if (!hasher.Verify(user.PasswordHash, request.Password))
        {
            user.RegisterFailedLogin(now);
            audit.Record(user.Id, "auth.login_failed", nameof(User), user.Id.ToString());
            await uow.SaveChangesAsync(ct);
            throw new AuthenticationFailedException();
        }

        user.RegisterSuccessfulLogin();
        var refresh = tokens.GenerateRefreshToken();
        refreshTokens.Add(new RefreshToken(user.Id, refresh.Hash, now, tokens.RefreshTokenLifetime));
        audit.Record(user.Id, "auth.login_succeeded", nameof(User), user.Id.ToString());
        await uow.SaveChangesAsync(ct);

        var access = tokens.CreateAccessToken(user);
        return new AuthTokensDto(access.Value, access.ExpiresAt, refresh.Raw);
    }
}
