using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Abstractions;
using WalletFlow.Domain.Accounts;
using WalletFlow.Domain.Auditing;
using WalletFlow.Domain.Transactions;
using WalletFlow.Domain.Users;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Email == normalizedEmail, ct);

    public void Add(User user) => db.Users.Add(user);
}

internal sealed class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct) =>
        db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);

    public async Task<bool> TryRevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken ct) =>
        await db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct) == 1;

    public Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
}

internal sealed class AccountRepository(AppDbContext db) : IAccountRepository
{
    public Task<Account?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<Account>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await db.Accounts.Where(a => ids.Contains(a.Id)).OrderBy(a => a.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<Account>> ListByOwnerAsync(Guid ownerId, CancellationToken ct) =>
        await db.Accounts.AsNoTracking().Where(a => a.OwnerId == ownerId).OrderBy(a => a.Currency).ThenBy(a => a.Id).ToListAsync(ct);

    public Task<int> CountByOwnerAsync(Guid ownerId, CancellationToken ct) =>
        db.Accounts.CountAsync(a => a.OwnerId == ownerId, ct);

    public void Add(Account account) => db.Accounts.Add(account);
}

internal sealed class TransactionRepository(AppDbContext db) : ITransactionRepository
{
    public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Transactions.Include(t => t.Entries).FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<(IReadOnlyList<Transaction> Items, int Total)> GetPageForAccountAsync(
        Guid accountId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Transactions.AsNoTracking()
            .Where(t => t.SourceAccountId == accountId || t.DestinationAccountId == accountId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public void Add(Transaction transaction) => db.Transactions.Add(transaction);
}

internal sealed class AuditLogRepository(AppDbContext db) : IAuditLogRepository
{
    public async Task<(IReadOnlyList<AuditLog> Items, int Total)> GetPageAsync(int page, int pageSize, CancellationToken ct)
    {
        var query = db.AuditLogs.AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(l => l.CreatedAt).ThenBy(l => l.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}
