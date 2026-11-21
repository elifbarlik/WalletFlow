using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace WalletFlow.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory factory)
{
    private readonly HttpClient _http = factory.CreateClient();

    [Fact]
    public async Task Health_Live_And_Ready_AreAnonymous()
    {
        (await _http.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _http.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        (await _http.GetAsync("/api/v1/accounts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithGarbageToken_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/accounts");
        request.Headers.Authorization = new("Bearer", "not.a.jwt");

        (await _http.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_WithWeakPassword_Returns400ProblemWithFieldErrors()
    {
        var response = await _http.PostAsJsonAsync("/api/v1/auth/register", new { email = "weak@example.test", password = "short" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Password");
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var user = await TestUser.RegisterAsync(_http);

        var response = await _http.PostAsJsonAsync("/api/v1/auth/register", new { email = user.Email.ToUpperInvariant(), password = "CorrectHorse123" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownUser_GiveTheSameGenericAnswer()
    {
        var user = await TestUser.RegisterAsync(_http);

        var wrong = await _http.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "WrongPassword123" });
        var unknown = await _http.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@example.test", password = "WrongPassword123" });

        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("Invalid credentials");
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("Invalid credentials");
    }

    [Fact]
    public async Task Login_AfterRepeatedFailures_LocksTheAccount()
    {
        var user = await TestUser.RegisterAsync(_http);
        for (var i = 0; i < 5; i++)
        {
            await _http.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "WrongPassword123" });
        }

        var correct = await _http.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "CorrectHorse123" });

        correct.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_RotatesTokens_AndReuseOfAnOldTokenRevokesTheFamily()
    {
        var user = await TestUser.RegisterAsync(_http);
        var first = user.Tokens.RefreshToken;

        var rotated = await _http.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first });
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = (await rotated.ReadAsync<TokensDto>()).RefreshToken;
        second.Should().NotBe(first);

        var replay = await _http.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first });
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The legitimate holder of the newer token is also logged out: theft is assumed.
        var afterTheft = await _http.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = second });
        afterTheft.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Responses_CarrySecurityHeaders_AndEchoSafeCorrelationIds()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "trace-abc-12345678");
        var response = await _http.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").Single().Should().Be("trace-abc-12345678");
        response.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Single().Should().Be("DENY");
        response.Headers.Contains("Server").Should().BeFalse();

        using var hostile = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        hostile.Headers.Add("X-Correlation-Id", "bad id with spaces");
        (await _http.SendAsync(hostile)).Headers.GetValues("X-Correlation-Id").Single().Should().NotContain(" ");
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemJson_NotAStackTrace()
    {
        var response = await _http.GetAsync("/api/v1/does-not-exist");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("at WalletFlow");
    }
}
