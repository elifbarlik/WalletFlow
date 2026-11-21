using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WalletFlow.Application.Abstractions;
using WalletFlow.Domain.Accounts;
using WalletFlow.Domain.Common;
using WalletFlow.Domain.Transactions;
using WalletFlow.Domain.Users;
using WalletFlow.Infrastructure.Persistence;

namespace WalletFlow.Infrastructure.Seeding;

/// <summary>
/// Creates fictional demo users. Runs only when <c>Seed:Enabled=true</c>; passwords come from configuration and
/// are never hard-coded here. Demo balances are created through real top-up transactions so the ledger stays consistent.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (!config.GetValue<bool>("Seed:Enabled"))
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        await EnsureUserAsync("admin@walletflow.test", "Seed:AdminPassword", Role.Admin);
        await EnsureUserAsync("auditor@walletflow.test", "Seed:AuditorPassword", Role.Auditor);
        var customer = await EnsureUserAsync("demo@walletflow.test", "Seed:DemoPassword", Role.Customer);

        if (customer is not null && !await db.Accounts.AnyAsync(a => a.OwnerId == customer.Id, ct))
        {
            var account = Account.Open(customer.Id, "TRY");
            var topUp = Transaction.StartTopUp(account, new Money(1000m, "TRY"), "demo seed", clock.UtcNow);
            topUp.CompleteTopUp(account);
            db.Accounts.Add(account);
            db.Transactions.Add(topUp);
            await db.SaveChangesAsync(ct);
        }

        async Task<User?> EnsureUserAsync(string email, string passwordKey, Role role)
        {
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
            if (existing is not null)
            {
                return existing;
            }

            var password = config[passwordKey];
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("Skipping seed user {Email}: {Key} is not configured", email, passwordKey);
                return null;
            }

            var user = User.Register(email, hasher.Hash(password), role, clock.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            return user;
        }
    }
}
