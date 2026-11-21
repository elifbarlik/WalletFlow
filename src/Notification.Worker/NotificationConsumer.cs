using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Notification.Worker;

public sealed class RabbitMqSettings
{
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
/// Consumes wallet events and "sends" notifications (logged here). Delivery from the outbox is at-least-once, so the
/// consumer is idempotent: it remembers recent message ids and acknowledges duplicates without acting on them.
/// Poison messages go to a dead-letter queue instead of looping forever. In production the dedupe set would live in a
/// shared store (inbox table or Redis) so it survives restarts and scales across instances.
/// </summary>
internal sealed class NotificationConsumer(IOptions<RabbitMqSettings> options, NotificationProcessor processor, ILogger<NotificationConsumer> logger) : BackgroundService
{
    private const string Queue = "walletflow.notifications";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = o.Host, Port = o.Port, UserName = o.Username, Password = o.Password,
            DispatchConsumersAsync = true, AutomaticRecoveryEnabled = true,
        };

        IConnection? connection = null;
        while (connection is null && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                connection = factory.CreateConnection("walletflow-notification-worker");
            }
            catch (Exception ex)
            {
                logger.LogWarning("RabbitMQ not reachable yet ({Reason}); retrying", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }

        if (connection is null)
        {
            return;
        }

        using (connection)
        using (var channel = connection.CreateModel())
        {
            var deadLetter = o.Exchange + ".dlx";
            channel.ExchangeDeclare(o.Exchange, ExchangeType.Topic, durable: true);
            channel.ExchangeDeclare(deadLetter, ExchangeType.Fanout, durable: true);
            channel.QueueDeclare(Queue + ".dead", durable: true, exclusive: false, autoDelete: false);
            channel.QueueBind(Queue + ".dead", deadLetter, string.Empty);
            channel.QueueDeclare(Queue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object> { ["x-dead-letter-exchange"] = deadLetter });
            channel.QueueBind(Queue, o.Exchange, "#");
            channel.BasicQos(0, prefetchCount: 10, global: false);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.Received += async (_, args) => await HandleAsync(channel, args);
            channel.BasicConsume(Queue, autoAck: false, consumer);
            logger.LogInformation("Notification worker consuming {Queue}", Queue);

            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task HandleAsync(IModel channel, BasicDeliverEventArgs args)
    {
        try
        {
            await processor.ProcessAsync(args.BasicProperties.MessageId, args.BasicProperties.Type, args.Body);
            channel.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not process message; dead-lettering");
            channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
        }
    }
}
