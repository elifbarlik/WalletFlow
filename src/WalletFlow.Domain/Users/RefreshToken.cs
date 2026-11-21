namespace WalletFlow.Domain.Users;

/// <summary>Opaque refresh token. Only the SHA-256 hash is stored; rotation replaces it on every use.</summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
        // Required by EF Core.
        TokenHash = string.Empty;
    }

    public RefreshToken(Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = now.Add(lifetime);
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
