using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace WalletFlow.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string Section = "Messaging:RabbitMq";

    [Required]
    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    [Required]
    public string Username { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    public string Exchange { get; init; } = "walletflow.events";
}

/// <summary>
/// Publishes outbox events to a durable topic exchange with persistent delivery and publisher confirms.
/// If the broker rejects or is unreachable, the exception surfaces and the outbox keeps the message for the next cycle.
/// </summary>
internal sealed class RabbitMqMessagePublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqMessagePublisher> logger)
    : IMessagePublisher, IDisposable
{
    private readonly object _gate = new();
    private IConnection? _connection;
    private IModel? _channel;

    public Task PublishAsync(Guid messageId, string type, string payload, CancellationToken ct)
    {
        lock (_gate)
        {
            try
            {
                var channel = EnsureChannel();
                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.MessageId = messageId.ToString();
                properties.Type = type;
                properties.ContentType = "application/json";
                channel.BasicPublish(options.Value.Exchange, routingKey: type, mandatory: false, properties, Encoding.UTF8.GetBytes(payload));
                channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
            }
            catch
            {
                Reset();
                throw;
            }
        }

        return Task.CompletedTask;
    }

    private IModel EnsureChannel()
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        Reset();
        var o = options.Value;
        var factory = new ConnectionFactory { HostName = o.Host, Port = o.Port, UserName = o.Username, Password = o.Password, DispatchConsumersAsync = true };
        _connection = factory.CreateConnection("walletflow-api");
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(o.Exchange, ExchangeType.Topic, durable: true);
        _channel.ConfirmSelect();
        logger.LogInformation("Connected to RabbitMQ at {Host}", o.Host);
        return _channel;
    }

    private void Reset()
    {
        try
        {
            _channel?.Dispose();
            _connection?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ignoring error while closing RabbitMQ connection");
        }

        _channel = null;
        _connection = null;
    }

    public void Dispose() => Reset();
}
