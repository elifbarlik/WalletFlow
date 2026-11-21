namespace WalletFlow.Application.Common;

/// <summary>Marks a command whose effect must happen at most once per (user, Idempotency-Key).</summary>
public interface IIdempotentRequest
{
    string IdempotencyKey { get; }
}
