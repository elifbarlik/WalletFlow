using WalletFlow.Domain.Common;

namespace WalletFlow.Domain.Users;

public sealed class User
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private User()
    {
        // Required by EF Core.
        Email = string.Empty;
        PasswordHash = string.Empty;
    }

    private User(string email, string passwordHash, Role role, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string PasswordHash { get; private set; }

    public Role Role { get; private set; }

    public int FailedLoginCount { get; private set; }

    public DateTimeOffset? LockoutEnd { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static User Register(string email, string passwordHash, Role role, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("user.invalid", "Email and password are required.");
        }

        return new User(NormalizeEmail(email), passwordHash, role, now);
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;

    public void RegisterFailedLogin(DateTimeOffset now)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedLogins)
        {
            LockoutEnd = now.Add(LockoutDuration);
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockoutEnd = null;
    }

    public void ChangePasswordHash(string passwordHash) => PasswordHash = passwordHash;
}
