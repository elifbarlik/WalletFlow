using System.Text;
using System.Text.Json;

namespace Notification.Worker;

/// <summary>Transport-independent handling of one event: dedupe through the inbox, then "send" the notification.</summary>
internal sealed class NotificationProcessor(IInbox inbox, ILogger<NotificationProcessor> logger)
{
    /// <returns>True when the notification was sent, false when the message was a duplicate.</returns>
    public async Task<bool> ProcessAsync(string? messageId, string? type, ReadOnlyMemory<byte> body)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new InvalidOperationException("Message has no MessageId.");
        }

        if (!await inbox.TryBeginAsync(messageId))
        {
            logger.LogInformation("Duplicate {EventType} {MessageId} ignored", type, messageId);
            return false;
        }

        try
        {
            using var json = JsonDocument.Parse(Encoding.UTF8.GetString(body.Span));
            var amount = json.RootElement.TryGetProperty("amount", out var a) ? a.GetDecimal() : 0m;
            var currency = json.RootElement.TryGetProperty("currency", out var c) ? c.GetString() : "?";
            logger.LogInformation("Notification sent for {EventType}: {Amount} {Currency} ({MessageId})", type, amount, currency, messageId);
            return true;
        }
        catch
        {
            await inbox.ForgetAsync(messageId); // let a redelivery try again
            throw;
        }
    }
}
