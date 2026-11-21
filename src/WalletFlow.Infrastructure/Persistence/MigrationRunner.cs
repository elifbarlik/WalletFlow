using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace WalletFlow.Infrastructure.Persistence;

public static class MigrationRunner
{
    private const long AdvisoryLockId = 727_274_001;

    /// <summary>
    /// Applies pending migrations while holding a PostgreSQL advisory lock, so several replicas starting at the
    /// same time (e.g. several containers behind a load balancer) cannot race each other.
    /// </summary>
    public static async Task MigrateWithLockAsync(this AppDbContext db, CancellationToken ct = default)
    {
        await using var lockConnection = new NpgsqlConnection(db.Database.GetConnectionString());
        await lockConnection.OpenAsync(ct);
        await using (var acquire = new NpgsqlCommand($"SELECT pg_advisory_lock({AdvisoryLockId})", lockConnection))
        {
            await acquire.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await db.Database.MigrateAsync(ct);
        }
        finally
        {
            await using var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({AdvisoryLockId})", lockConnection);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
