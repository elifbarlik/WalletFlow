using WalletFlow.Domain.Users;

namespace WalletFlow.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ICurrentUser
{
    /// <summary>Authenticated user id. Throws <see cref="Common.AuthenticationFailedException"/> when anonymous.</summary>
    Guid UserId { get; }

    Role Role { get; }

    bool IsAdmin => Role == Role.Admin;
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string hash, string password);

    /// <summary>Spends the same time as a real verification so unknown e-mails are not distinguishable by latency.</summary>
    void SimulateVerification(string password);
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public sealed record RefreshTokenPair(string Raw, string Hash);

public interface ITokenService
{
    TimeSpan RefreshTokenLifetime { get; }

    AccessToken CreateAccessToken(User user);

    RefreshTokenPair GenerateRefreshToken();

    string HashRefreshToken(string rawToken);
}

public sealed record PaymentRequest(Guid PaymentId, decimal Amount, string Currency, string SourceToken, string Description);

public sealed record PaymentResult(bool Approved, string? ProviderReference, string? DeclineReason);

public interface IPaymentGateway
{
    /// <summary>Charges the customer's payment method at the external provider.</summary>
    /// <exception cref="Common.ExternalServiceException">The provider is unreachable or failing.</exception>
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct);
}

public interface IOutbox
{
    /// <summary>Stages an event; it is persisted with the current unit of work.</summary>
    void Enqueue<TEvent>(TEvent integrationEvent)
        where TEvent : Events.IIntegrationEvent;
}

public interface IAuditTrail
{
    /// <summary>Stages an audit entry; it is persisted with the current unit of work.</summary>
    void Record(Guid? userId, string action, string entityType, string? entityId = null, string? details = null);
}
