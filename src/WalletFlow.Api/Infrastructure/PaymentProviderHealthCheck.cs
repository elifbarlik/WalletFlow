using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WalletFlow.Infrastructure.Payments;

namespace WalletFlow.Api.Infrastructure;

/// <summary>The provider is optional for readiness: when it is down the API reports Degraded, not Unhealthy.</summary>
internal sealed class PaymentProviderHealthCheck(IHttpClientFactory factory, IOptions<PaymentProviderOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var client = factory.CreateClient("payment-health");
            client.Timeout = TimeSpan.FromSeconds(2);
            using var response = await client.GetAsync(new Uri(new Uri(options.Value.BaseUrl.TrimEnd('/') + "/"), "health"), ct);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded("Payment provider responded with an error.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Degraded("Payment provider is unreachable.");
        }
    }
}
