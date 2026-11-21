using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Domain.Users;

namespace WalletFlow.Api.Infrastructure;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal Principal =>
        accessor.HttpContext?.User ?? throw new AuthenticationFailedException();

    public Guid UserId =>
        Guid.TryParse(Principal.FindFirst("sub")?.Value, out var id) ? id : throw new AuthenticationFailedException();

    public Role Role =>
        Enum.TryParse<Role>(Principal.FindFirst("role")?.Value, ignoreCase: false, out var role) ? role : throw new AuthenticationFailedException();
}
