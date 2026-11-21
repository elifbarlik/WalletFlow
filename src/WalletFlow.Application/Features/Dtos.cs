using WalletFlow.Domain.Accounts;
using WalletFlow.Domain.Auditing;
using WalletFlow.Domain.Transactions;
using WalletFlow.Domain.Users;

namespace WalletFlow.Application.Features;

public sealed record UserDto(Guid Id, string Email, string Role);

public sealed record AuthTokensDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);

public sealed record AccountDto(Guid Id, string Currency, decimal Balance, string Status)
{
    public static AccountDto From(Account account) =>
        new(account.Id, account.Currency, account.Balance, account.Status.ToString());
}

public sealed record TransactionDto(
    Guid Id,
    string Type,
    string Status,
    decimal Amount,
    string Currency,
    string Reference,
    string? FailureReason,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    DateTimeOffset CreatedAt)
{
    public static TransactionDto From(Transaction t) =>
        new(t.Id, t.Type.ToString(), t.Status.ToString(), t.Amount, t.Currency, t.Reference, t.FailureReason,
            t.SourceAccountId, t.DestinationAccountId, t.CreatedAt);
}

public sealed record AuditLogDto(Guid Id, Guid? UserId, string Action, string EntityType, string? EntityId, DateTimeOffset CreatedAt)
{
    public static AuditLogDto From(AuditLog log) =>
        new(log.Id, log.UserId, log.Action, log.EntityType, log.EntityId, log.CreatedAt);
}

internal static class UserMapping
{
    public static UserDto ToDto(this User user) => new(user.Id, user.Email, user.Role.ToString());
}
