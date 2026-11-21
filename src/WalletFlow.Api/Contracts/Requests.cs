using System.ComponentModel;
using WalletFlow.Domain.Accounts;

namespace WalletFlow.Api.Contracts;

/// <summary>Public request bodies. Kept separate from application commands so the HTTP contract can evolve on its own.</summary>
public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record OpenAccountRequest([property: DefaultValue("TRY")] string Currency);

public sealed record TopUpRequest(decimal Amount, [property: DefaultValue("tok_visa")] string SourceToken);

public sealed record TransferRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount, string? Reference);

public sealed record SetAccountStatusRequest(AccountStatus Status);
