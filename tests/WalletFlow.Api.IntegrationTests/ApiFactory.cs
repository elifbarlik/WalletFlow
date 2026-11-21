using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;

namespace WalletFlow.Api.IntegrationTests;

/// <summary>
/// Boots the real API against a real PostgreSQL (Testcontainers). Set TEST_DB_CONNECTION to reuse an existing
/// server instead of starting a container. Only the external payment provider is replaced by a fake.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminPassword = "AdminPass#2026x";
    public const string AuditorPassword = "AuditPass#2026x";

    private readonly PostgreSqlContainer? _container;
    private RedisContainer? _redis;
    private readonly string? _externalConnection = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION");

    public ApiFactory()
    {
        if (_externalConnection is null)
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        }
    }

    public FakePaymentGateway Payments { get; } = new();

    /// <summary>When true the host uses Redis for idempotency keys and distributed rate limiting.</summary>
    protected virtual bool UseRedis => false;

    public async Task InitializeAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync();
        }

        if (UseRedis)
        {
            _redis = new RedisBuilder("redis:7-alpine").Build();
            await _redis.StartAsync();
        }
    }

    public new async Task DisposeAsync()
    {
        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", _externalConnection ?? _container!.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-0123456789-abcdef");
        builder.UseSetting("PaymentProvider:BaseUrl", "http://127.0.0.1:9");
        builder.UseSetting("PaymentProvider:ApiKey", "integration-test-api-key");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Seed:AuditorPassword", AuditorPassword);
        builder.UseSetting("Seed:DemoPassword", "DemoPass#2026xy");
        builder.UseSetting("RateLimiting:Scale", "1000");
        if (_redis is not null)
        {
            builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
            builder.UseSetting("Idempotency:Provider", "Redis");
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(Payments);
        });
    }
}

/// <summary>Deterministic payment provider: tok_declined declines, tok_down is unavailable, everything else is approved.</summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    private int _calls;

    public int Calls => _calls;

    public Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        return request.SourceToken switch
        {
            "tok_declined" => Task.FromResult(new PaymentResult(false, null, "card_declined")),
            "tok_down" => throw new ExternalServiceException("Payment provider is currently unavailable."),
            _ => Task.FromResult(new PaymentResult(true, $"ch_{request.PaymentId:N}", null)),
        };
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

public sealed class RedisApiFactory : ApiFactory
{
    protected override bool UseRedis => true;
}

[CollectionDefinition(Name)]
public sealed class RedisApiCollection : ICollectionFixture<RedisApiFactory>
{
    public const string Name = "redis-api";
}


