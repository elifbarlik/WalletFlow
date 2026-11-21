using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Domain.Idempotency;

namespace WalletFlow.Infrastructure.Persistence;

/// <summary>Durable, zero-extra-infrastructure idempotency store. The unique (user, key) index makes reservation race-safe.</summary>
internal sealed class IdempotencyStore(AppDbContext db) : IIdempotencyStore
{
    public async Task<IdempotencyEntry?> FindAsync(Guid userId, string key, CancellationToken ct)
    {
        var record = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, ct);
        return record is null
            ? null
            : new IdempotencyEntry(record.RequestName, record.RequestHash, record.IsCompleted, record.ResponseJson, record.CreatedAt);
    }

    public async Task<bool> TryReserveAsync(Guid userId, string key, IdempotencyEntry entry, CancellationToken ct)
    {
        var record = new IdempotencyRecord(userId, key, entry.RequestName, entry.RequestHash, entry.CreatedAt);
        db.IdempotencyRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
            db.Entry(record).State = EntityState.Detached;
            return true;
        }
        catch (DuplicateKeyException)
        {
            db.Entry(record).State = EntityState.Detached;
            return false;
        }
    }

    public Task CompleteAsync(Guid userId, string key, string responseJson, CancellationToken ct) =>
        db.IdempotencyRecords.Where(r => r.UserId == userId && r.Key == key)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsCompleted, true).SetProperty(r => r.ResponseJson, responseJson), ct);

    public Task ReleaseAsync(Guid userId, string key, CancellationToken ct) =>
        db.IdempotencyRecords.Where(r => r.UserId == userId && r.Key == key).ExecuteDeleteAsync(ct);
}
