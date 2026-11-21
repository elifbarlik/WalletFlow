using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using WalletFlow.Application.Abstractions;
using WalletFlow.Infrastructure.Messaging;
using WalletFlow.Infrastructure.Payments;
using StackExchange.Redis;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Infrastructure.Redis;
using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(config.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured."))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        var redisConnection = config.GetConnectionString(RedisOptions.ConnectionStringName);
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var options = ConfigurationOptions.Parse(redisConnection);
                options.AbortOnConnectFail = false; // keep the API up and reconnect in the background
                options.ClientName = "walletflow-api";
                return ConnectionMultiplexer.Connect(options);
            });
            services.AddSingleton<IDistributedRateLimiterFactory, RedisRateLimiterFactory>();
        }

        var idempotencyProvider = config["Idempotency:Provider"] ?? "Postgres";
        if (string.Equals(idempotencyProvider, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(redisConnection))
            {
                throw new InvalidOperationException("Idempotency:Provider=Redis requires ConnectionStrings:Redis.");
            }

            services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        }
        else
        {
            services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        }
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<IOutbox, Outbox>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<Application.Abstractions.IPasswordHasher, Pbkdf2PasswordHasher>();

        services.AddOptions<PaymentProviderOptions>().Bind(config.GetSection(PaymentProviderOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddHttpClient<IPaymentGateway, PaymentGatewayClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymentProviderOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
            })
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 3;
                o.Retry.Delay = TimeSpan.FromMilliseconds(200);
                o.Retry.UseJitter = true;
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                o.CircuitBreaker.MinimumThroughput = 6;
                o.CircuitBreaker.FailureRatio = 0.5;
                o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(20);
            });

        if (string.Equals(config["Messaging:Provider"], "RabbitMq", StringComparison.OrdinalIgnoreCase))
        {
            services.AddOptions<RabbitMqOptions>().Bind(config.GetSection(RabbitMqOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
            services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>();
        }
        else
        {
            services.AddSingleton<IMessagePublisher, LoggingMessagePublisher>();
        }
        services.AddHostedService<OutboxDispatcher>();
        return services;
    }
}



