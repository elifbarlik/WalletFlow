using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Application.Events;
using WalletFlow.Domain.Accounts;
using WalletFlow.Domain.Transactions;
using WalletFlow.Domain.Users;

namespace WalletFlow.Application.Tests;

internal sealed record TestUser(Guid UserId, Role Role = Role.Customer) : ICurrentUser;

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>One in-memory stand-in for persistence, outbox and audit so handlers can be tested without a database.</summary>
internal sealed class InMemoryStore : IAccountRepository, ITransactionRepository, IUserRepository, IRefreshTokenRepository,
    IUnitOfWork, IAuditTrail, IOutbox, IIdempotencyStore
{
    public List<Account> Accounts { get; } = [];

    public List<Transaction> Transactions { get; } = [];

    public List<User> Users { get; } = [];

    public List<RefreshToken> RefreshTokens { get; } = [];

    public List<IIntegrationEvent> Events { get; } = [];

    public List<string> AuditActions { get; } = [];

    public Dictionary<(Guid User, string Key), IdempotencyEntry> Entries { get; } = [];

    // ---- accounts
    Task<Account?> IAccountRepository.GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Accounts.FirstOrDefault(a => a.Id == id));

    public Task<IReadOnlyList<Account>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Account>>(Accounts.Where(a => ids.Contains(a.Id)).OrderBy(a => a.Id).ToList());

    public Task<IReadOnlyList<Account>> ListByOwnerAsync(Guid ownerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Account>>(Accounts.Where(a => a.OwnerId == ownerId).ToList());

    public Task<int> CountByOwnerAsync(Guid ownerId, CancellationToken ct) =>
        Task.FromResult(Accounts.Count(a => a.OwnerId == ownerId));

    public void Add(Account account) => Accounts.Add(account);

    // ---- transactions
    Task<Transaction?> ITransactionRepository.GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.Id == id));

    public Task<(IReadOnlyList<Transaction> Items, int Total)> GetPageForAccountAsync(Guid accountId, int page, int pageSize, CancellationToken ct)
    {
        var all = Transactions.Where(t => t.SourceAccountId == accountId || t.DestinationAccountId == accountId).ToList();
        return Task.FromResult<(IReadOnlyList<Transaction>, int)>((all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), all.Count));
    }

    public void Add(Transaction transaction)
    {
        // Mirrors the primary-key constraint that backs idempotency in the real database.
        if (Transactions.Any(t => t.Id == transaction.Id))
        {
            throw new DuplicateKeyException();
        }

        Transactions.Add(transaction);
    }

    // ---- users & tokens
    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Email == normalizedEmail));

    Task<User?> IUserRepository.GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct) =>
        Task.FromResult(Users.Any(u => u.Email == normalizedEmail));

    public void Add(User user) => Users.Add(user);

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(RefreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash));

    public void Add(RefreshToken token) => RefreshTokens.Add(token);

    public Task<bool> TryRevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken ct)
    {
        var token = RefreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash && t.IsActive(now));
        token?.Revoke(now);
        return Task.FromResult(token is not null);
    }

    public Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        RefreshTokens.Where(t => t.UserId == userId).ToList().ForEach(t => t.Revoke(now));
        return Task.CompletedTask;
    }

    // ---- unit of work / audit / outbox
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) => action(ct);

    public void Record(Guid? userId, string action, string entityType, string? entityId = null, string? details = null) =>
        AuditActions.Add(action);

    public void Enqueue<TEvent>(TEvent integrationEvent)
        where TEvent : IIntegrationEvent => Events.Add(integrationEvent);

    // ---- idempotency
    public Task<IdempotencyEntry?> FindAsync(Guid userId, string key, CancellationToken ct) =>
        Task.FromResult(Entries.GetValueOrDefault((userId, key)));

    public Task<bool> TryReserveAsync(Guid userId, string key, IdempotencyEntry entry, CancellationToken ct) =>
        Task.FromResult(Entries.TryAdd((userId, key), entry));

    public Task CompleteAsync(Guid userId, string key, string responseJson, CancellationToken ct)
    {
        Entries[(userId, key)] = Entries[(userId, key)] with { IsCompleted = true, ResponseJson = responseJson };
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(Guid userId, string key, CancellationToken ct)
    {
        Entries.Remove((userId, key));
        return Task.CompletedTask;
    }
}
