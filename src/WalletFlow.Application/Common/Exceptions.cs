namespace WalletFlow.Application.Common;

/// <summary>Base class for expected, client-facing failures. The API maps each subtype to an HTTP status.</summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class NotFoundException(string message = "Resource not found.")
    : AppException("resource.not_found", message);

public sealed class AuthenticationFailedException()
    : AppException("auth.invalid_credentials", "Invalid credentials.");

public sealed class ForbiddenException(string message = "You are not allowed to perform this action.")
    : AppException("auth.forbidden", message);

public sealed class ConflictException(string code, string message) : AppException(code, message);

public sealed class DuplicateKeyException()
    : AppException("conflict.duplicate", "A resource with the same unique value already exists.");

public sealed class ConcurrencyConflictException()
    : AppException("conflict.concurrency", "The resource was modified concurrently. Please retry.");

public sealed class RequestInProgressException()
    : AppException("idempotency.in_progress", "A request with this Idempotency-Key is still being processed.");

public sealed class IdempotencyKeyReusedException()
    : AppException("idempotency.key_reused", "This Idempotency-Key was already used with a different request.");

public sealed class PaymentDeclinedException(string reason)
    : AppException("payment.declined", $"Payment was declined: {reason}");

public sealed class ExternalServiceException(string message)
    : AppException("external.unavailable", message);
