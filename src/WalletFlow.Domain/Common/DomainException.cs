namespace WalletFlow.Domain.Common;

/// <summary>Raised when a business rule of the domain is violated.</summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message) : base(message) => Code = code;

    /// <summary>Stable machine-readable error code (mapped to ProblemDetails by the API).</summary>
    public string Code { get; }
}
