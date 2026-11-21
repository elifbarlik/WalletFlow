using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Domain.Accounts;

namespace WalletFlow.Application.Features.Accounts;

internal static class AccountAccess
{
    /// <summary>
    /// Object-level authorization. Someone else's account is reported as "not found" so that
    /// account ids cannot be probed for existence.
    /// </summary>
    public static Account EnsureOwnedBy(this Account? account, ICurrentUser user, bool allowAdmin = false)
    {
        if (account is null || (account.OwnerId != user.UserId && !(allowAdmin && user.IsAdmin)))
        {
            throw new NotFoundException("Account not found.");
        }

        return account;
    }
}
