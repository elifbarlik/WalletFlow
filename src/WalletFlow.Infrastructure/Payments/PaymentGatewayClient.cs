using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Polly;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;

namespace WalletFlow.Infrastructure.Payments;

/// <summary>
/// Typed HttpClient for the external payment provider. Retries, circuit breaker and timeouts are applied by the
/// resilience pipeline registered in DI. Retrying a POST is safe because every call carries the payment id as
/// <c>Idempotency-Key</c>, which the provider uses to deduplicate.
/// </summary>
internal sealed class PaymentGatewayClient(HttpClient http) : IPaymentGateway
{
    public async Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/charges")
        {
            Content = JsonContent.Create(new ChargeBody(request.Amount, request.Currency, request.SourceToken, request.Description)),
        };
        message.Headers.Add("Idempotency-Key", request.PaymentId.ToString());

        try
        {
            using var response = await http.SendAsync(message, ct);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<ChargeResponse>(ct);
                return new PaymentResult(true, body?.Id, null);
            }

            if (response.StatusCode == HttpStatusCode.PaymentRequired)
            {
                var body = await response.Content.ReadFromJsonAsync<ChargeResponse>(ct);
                return new PaymentResult(false, null, body?.Error ?? "declined");
            }

            throw new ExternalServiceException($"Payment provider responded with {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or ExecutionRejectedException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new ExternalServiceException("Payment provider is currently unavailable.");
        }
    }

    private sealed record ChargeBody(decimal Amount, string Currency, string Source, string Description);

    private sealed record ChargeResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("error")] string? Error);
}
