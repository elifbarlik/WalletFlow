using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WalletFlow.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>. Reads the connection string from the environment; no credentials in source.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("WALLETFLOW_DESIGN_CONNECTION")
            ?? "Host=localhost;Database=walletflow_design;Username=design;Password=design";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }
}
