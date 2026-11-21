using FluentAssertions;
using NSubstitute;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Application.Features.Auth;
using WalletFlow.Domain.Users;

namespace WalletFlow.Application.Tests;

public class AuthHandlerTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly InMemoryStore _store = new();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();

    public AuthHandlerTests()
    {
        _tokens.RefreshTokenLifetime.Returns(TimeSpan.FromDays(7));
        _tokens.GenerateRefreshToken().Returns(new RefreshTokenPair("raw", "hash"));
        _tokens.CreateAccessToken(Arg.Any<User>()).Returns(new AccessToken("jwt", Clock.UtcNow.AddMinutes(15)));
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");
    }

    private LoginHandler Login() => new(_store, _store, _hasher, _tokens, _store, _store, Clock);

    private User AddUser(string email = "a@b.test")
    {
        var user = User.Register(email, "hashed", Role.Customer, Clock.UtcNow);
        _store.Users.Add(user);
        return user;
    }

    [Fact]
    public async Task Register_AlwaysCreatesCustomer_AndRejectsDuplicates()
    {
        var handler = new RegisterHandler(_store, _hasher, _store, _store, Clock);

        var dto = await handler.Handle(new RegisterCommand("New@Example.test", "Password12345"), default);
        var duplicate = () => handler.Handle(new RegisterCommand("new@example.TEST", "Password12345"), default);

        dto.Role.Should().Be("Customer");
        dto.Email.Should().Be("new@example.test");
        await duplicate.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Login_WithUnknownEmail_FailsGenericallyAndBurnsTime()
    {
        var act = () => Login().Handle(new LoginCommand("ghost@x.test", "whatever"), default);

        await act.Should().ThrowAsync<AuthenticationFailedException>();
        _hasher.Received(1).SimulateVerification("whatever");
    }

    [Fact]
    public async Task Login_AfterFiveFailures_LocksOutEvenWithCorrectPassword()
    {
        var user = AddUser();
        _hasher.Verify("hashed", "wrong").Returns(false);
        _hasher.Verify("hashed", "right").Returns(true);
        for (var i = 0; i < User.MaxFailedLogins; i++)
        {
            var failed = () => Login().Handle(new LoginCommand(user.Email, "wrong"), default);
            await failed.Should().ThrowAsync<AuthenticationFailedException>();
        }

        var locked = () => Login().Handle(new LoginCommand(user.Email, "right"), default);

        await locked.Should().ThrowAsync<AuthenticationFailedException>();
        _hasher.DidNotReceive().Verify("hashed", "right");
        _store.RefreshTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Login_Success_IssuesTokensAndStoresOnlyTheHash()
    {
        var user = AddUser();
        _hasher.Verify("hashed", "right").Returns(true);

        var result = await Login().Handle(new LoginCommand(user.Email, "right"), default);

        result.AccessToken.Should().Be("jwt");
        result.RefreshToken.Should().Be("raw");
        _store.RefreshTokens.Should().ContainSingle().Which.TokenHash.Should().Be("hash");
    }

    [Fact]
    public async Task Refresh_ReusingARevokedToken_RevokesTheWholeFamily()
    {
        var user = AddUser();
        var old = new RefreshToken(user.Id, "old-hash", Clock.UtcNow, TimeSpan.FromDays(1));
        old.Revoke(Clock.UtcNow);
        var live = new RefreshToken(user.Id, "live-hash", Clock.UtcNow, TimeSpan.FromDays(1));
        _store.RefreshTokens.AddRange([old, live]);
        _tokens.HashRefreshToken("stolen").Returns("old-hash");
        var handler = new RefreshTokenHandler(_store, _store, _tokens, _store, _store, Clock);

        var act = () => handler.Handle(new RefreshTokenCommand("stolen"), default);

        await act.Should().ThrowAsync<AuthenticationFailedException>();
        live.RevokedAt.Should().NotBeNull();
        _store.AuditActions.Should().Contain("auth.refresh_token_reuse_detected");
    }
}
