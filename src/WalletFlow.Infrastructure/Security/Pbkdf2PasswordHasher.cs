using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using WalletFlow.Application.Abstractions;

namespace WalletFlow.Infrastructure.Security;

/// <summary>PBKDF2-HMAC-SHA512 with a per-password salt and 210,000 iterations (OWASP guidance).</summary>
internal sealed class Pbkdf2PasswordHasher : Application.Abstractions.IPasswordHasher
{
    private static readonly object Subject = new();

    private readonly PasswordHasher<object> _inner =
        new(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));

    private readonly string _dummyHash;

    public Pbkdf2PasswordHasher() => _dummyHash = _inner.HashPassword(Subject, Guid.NewGuid().ToString());

    public string Hash(string password) => _inner.HashPassword(Subject, password);

    public bool Verify(string hash, string password) =>
        _inner.VerifyHashedPassword(Subject, hash, password) is not PasswordVerificationResult.Failed;

    public void SimulateVerification(string password) => _inner.VerifyHashedPassword(Subject, _dummyHash, password);
}
