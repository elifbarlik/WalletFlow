using System.Data;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;

namespace WalletFlow.Infrastructure.Persistence;

internal sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    private const int MaxAttempts = 10;

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            // Always start clean so a retry re-reads fresh rows instead of reusing stale tracked entities.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                var result = await action(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                // Randomized backoff keeps competing writers from colliding in lock-step.
                await Task.Delay(Random.Shared.Next(5, 40 * attempt), ct);
            }
            catch
            {
                // Do not leave half-built entities in the tracker for whatever the caller does next.
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                throw;
            }
        }
    }
}

