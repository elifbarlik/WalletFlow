using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Infrastructure.Payments;

public sealed class PaymentProviderOptions
{
    public const string Section = "PaymentProvider";

    [Required]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Shared secret sent as <c>X-Api-Key</c>. Provide it via environment or secret store.</summary>
    [Required]
    [MinLength(16)]
    public string ApiKey { get; init; } = string.Empty;
}
