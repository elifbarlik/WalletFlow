using Notification.Worker;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

if (builder.Configuration["Redis:ConnectionString"] is { Length: > 0 } redis)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var options = ConfigurationOptions.Parse(redis);
        options.AbortOnConnectFail = false;
        options.ClientName = "walletflow-notification-worker";
        return ConnectionMultiplexer.Connect(options);
    });
    builder.Services.AddSingleton<IInbox, RedisInbox>();
}
else
{
    builder.Services.AddSingleton<IInbox, InMemoryInbox>();
}

builder.Services.AddSingleton<NotificationProcessor>();

builder.Services.AddOptions<RabbitMqSettings>().Bind(builder.Configuration.GetSection("RabbitMq")).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddHostedService<NotificationConsumer>();
await builder.Build().RunAsync();

