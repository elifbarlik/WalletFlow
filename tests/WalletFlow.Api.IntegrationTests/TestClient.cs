using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WalletFlow.Api.IntegrationTests;

public sealed record TokensDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);

public sealed record AccountResponse(Guid Id, string Currency, decimal Balance, string Status);

public sealed record TransactionResponse(Guid Id, string Type, string Status, decimal Amount, string Currency);

public sealed record PageResponse<T>(List<T> Items, int Page, int PageSize, int TotalCount);

public sealed record ProblemResponse(int Status, string Title, string? Detail, string? Code, string? TraceId);

/// <summary>Small helper that keeps the tests readable: a client bound to one user.</summary>
public sealed class TestUser(HttpClient http, string email, TokensDto tokens)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string Email { get; } = email;

    public TokensDto Tokens { get; private set; } = tokens;

    public static async Task<TestUser> RegisterAsync(HttpClient http)
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        const string password = "CorrectHorse123";
        (await http.PostAsJsonAsync("/api/v1/auth/register", new { email, password })).EnsureSuccessStatusCode();
        return await LoginAsync(http, email, password);
    }

    public static async Task<TestUser> LoginAsync(HttpClient http, string email, string password)
    {
        var response = await http.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return new TestUser(http, email, (await response.Content.ReadFromJsonAsync<TokensDto>(Json))!);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, string? idempotencyKey = null, bool anonymous = false)
    {
        var request = new HttpRequestMessage(method, url);
        if (!anonymous)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Tokens.AccessToken);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        return http.SendAsync(request);
    }

    public Task<HttpResponseMessage> GetAsync(string url) => SendAsync(HttpMethod.Get, url);

    public Task<HttpResponseMessage> PostAsync(string url, object body, string? key = null) =>
        SendAsync(HttpMethod.Post, url, body, key);

    public async Task<AccountResponse> OpenAccountAsync(string currency = "TRY")
    {
        var response = await PostAsync("/api/v1/accounts", new { currency });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(Json))!;
    }

    public async Task<AccountResponse> OpenFundedAccountAsync(decimal amount)
    {
        var account = await OpenAccountAsync();
        (await PostAsync($"/api/v1/accounts/{account.Id}/topups", new { amount, sourceToken = "tok_visa" }, NewKey())).EnsureSuccessStatusCode();
        return await GetAccountAsync(account.Id);
    }

    public async Task<AccountResponse> GetAccountAsync(Guid id)
    {
        var response = await GetAsync($"/api/v1/accounts/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(Json))!;
    }

    public Task<HttpResponseMessage> TransferAsync(Guid from, Guid to, decimal amount, string? key = null) =>
        PostAsync("/api/v1/transfers", new { fromAccountId = from, toAccountId = to, amount, reference = "test" }, key ?? NewKey());

    public static string NewKey() => $"k-{Guid.NewGuid():N}";
}

public static class HttpExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;
}
