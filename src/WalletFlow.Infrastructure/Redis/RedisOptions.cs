namespace WalletFlow.Infrastructure.Redis;

public sealed class RedisOptions
{
    public const string ConnectionStringName = "Redis";

    /// <summary>How long an in-flight request may hold its key before it is considered abandoned.</summary>
    public static readonly TimeSpan InProgressTtl = TimeSpan.FromMinutes(5);

    /// <summary>How long a finished response can be replayed.</summary>
    public static readonly TimeSpan CompletedTtl = TimeSpan.FromHours(24);
}
