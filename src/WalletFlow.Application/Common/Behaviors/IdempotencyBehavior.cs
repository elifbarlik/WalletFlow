using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using WalletFlow.Application.Abstractions;

namespace WalletFlow.Application.Common.Behaviors;

/// <summary>
/// Makes commands marked with <see cref="IIdempotentRequest"/> safe to retry. The key is reserved first
/// (unique per user), the handler runs once, and the serialized response is replayed for every retry.
/// A different payload under the same key is rejected; a concurrent duplicate gets 409.
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse>(
    IIdempotencyStore store,
    ICurrentUser currentUser,
    IClock clock)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>An in-progress reservation older than this is considered abandoned (e.g. process crash).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IIdempotentRequest idempotent)
        {
            return await next();
        }

        var userId = currentUser.UserId;
        var key = idempotent.IdempotencyKey;
        var requestName = typeof(TRequest).Name;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, request.GetType(), Json))));

        if (await TryReplayAsync(userId, key, requestName, hash, ct) is { } replay)
        {
            return replay.Value;
        }

        var reserved = await store.TryReserveAsync(userId, key, new IdempotencyEntry(requestName, hash, false, null, clock.UtcNow), ct);
        if (!reserved)
        {
            // Lost a race against a concurrent request with the same key.
            if (await TryReplayAsync(userId, key, requestName, hash, ct) is { } raced)
            {
                return raced.Value;
            }

            throw new RequestInProgressException();
        }

        TResponse response;
        try
        {
            response = await next();
        }
        catch
        {
            // The command failed definitively: free the key so the client may retry.
            await store.ReleaseAsync(userId, key, CancellationToken.None);
            throw;
        }

        await store.CompleteAsync(userId, key, JsonSerializer.Serialize(response, Json), CancellationToken.None);
        return response;
    }

    private async Task<Replay?> TryReplayAsync(Guid userId, string key, string requestName, string hash, CancellationToken ct)
    {
        var existing = await store.FindAsync(userId, key, ct);
        if (existing is null)
        {
            return null;
        }

        if (existing.RequestName != requestName || existing.RequestHash != hash)
        {
            throw new IdempotencyKeyReusedException();
        }

        if (existing.IsCompleted)
        {
            return new Replay(JsonSerializer.Deserialize<TResponse>(existing.ResponseJson!, Json)!);
        }

        if (clock.UtcNow - existing.CreatedAt < StaleAfter)
        {
            throw new RequestInProgressException();
        }

        await store.ReleaseAsync(userId, key, ct);
        return null;
    }

    private readonly record struct Replay(TResponse Value);
}

