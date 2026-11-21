using WalletFlow.Domain.Accounts;
using WalletFlow.Domain.Auditing;
using WalletFlow.Domain.Transactions;
using WalletFlow.Domain.Users;

namespace WalletFlow.Application.Abstractions;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>
    /// Runs <paramref name="action"/> in one database transaction. Starts with an empty change tracker and retries
    /// the whole action on optimistic-concurrency conflicts, so the action must load its data itself.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
}

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct);

    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct);

    void Add(User user);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct);

    void Add(RefreshToken token);

    /// <summary>Atomically revokes the token if it is still active. Returns false when it was not active.</summary>
    Task<bool> TryRevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken ct);

    Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken ct);
}

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Loads the given accounts (tracked) in a deterministic id order.</summary>
    Task<IReadOnlyList<Account>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    Task<IReadOnlyList<Account>> ListByOwnerAsync(Guid ownerId, CancellationToken ct);

    Task<int> CountByOwnerAsync(Guid ownerId, CancellationToken ct);

    void Add(Account account);
}

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<(IReadOnlyList<Transaction> Items, int Total)> GetPageForAccountAsync(
        Guid accountId, int page, int pageSize, CancellationToken ct);

    void Add(Transaction transaction);
}

public interface IAuditLogRepository
{
    Task<(IReadOnlyList<AuditLog> Items, int Total)> GetPageAsync(int page, int pageSize, CancellationToken ct);
}

/// <summary>Storage-agnostic view of a remembered request (backed by PostgreSQL or Redis).</summary>
public sealed record IdempotencyEntry(
    string RequestName,
    string RequestHash,
    bool IsCompleted,
    string? ResponseJson,
    DateTimeOffset CreatedAt);

public interface IIdempotencyStore
{
    Task<IdempotencyEntry?> FindAsync(Guid userId, string key, CancellationToken ct);

    /// <summary>Atomically stores the entry if the key is free. Returns false when another request already holds it.</summary>
    Task<bool> TryReserveAsync(Guid userId, string key, IdempotencyEntry entry, CancellationToken ct);

    Task CompleteAsync(Guid userId, string key, string responseJson, CancellationToken ct);

    Task ReleaseAsync(Guid userId, string key, CancellationToken ct);
}
