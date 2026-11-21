using System.Security.Cryptography;
using System.Text;

namespace WalletFlow.Application.Common;

/// <summary>
/// Derives a stable transaction id from (user, operation, Idempotency-Key). Because the id is the primary key, the
/// database itself rejects a second transaction for the same key. That makes the guarantee survive even if the key
/// store (Redis) loses its data, and it doubles as the idempotency key sent to the payment provider.
/// </summary>
public static class IdempotentIds
{
    public static Guid For(Guid userId, string operation, string idempotencyKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}|{operation}|{idempotencyKey}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
