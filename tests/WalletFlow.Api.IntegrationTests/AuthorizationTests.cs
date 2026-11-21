using System.Net;
using FluentAssertions;

namespace WalletFlow.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthorizationTests(ApiFactory factory)
{
    private readonly HttpClient _http = factory.CreateClient();

    [Fact]
    public async Task Customer_CannotReadAnotherCustomersAccount_AndGets404NotForbidden()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var bob = await TestUser.RegisterAsync(_http);
        var aliceAccount = await alice.OpenAccountAsync();

        var response = await bob.GetAsync($"/api/v1/accounts/{aliceAccount.Id}");
        var history = await bob.GetAsync($"/api/v1/accounts/{aliceAccount.Id}/transactions");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        history.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Customer_CannotTransferFromAnotherCustomersAccount()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var bob = await TestUser.RegisterAsync(_http);
        var aliceAccount = await alice.OpenFundedAccountAsync(100m);
        var bobAccount = await bob.OpenAccountAsync();

        var response = await bob.TransferAsync(aliceAccount.Id, bobAccount.Id, 50m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await alice.GetAccountAsync(aliceAccount.Id)).Balance.Should().Be(100m);
    }

    [Fact]
    public async Task Customer_CannotUseAdminEndpoints()
    {
        var customer = await TestUser.RegisterAsync(_http);
        var account = await customer.OpenAccountAsync();

        (await customer.SendAsync(HttpMethod.Patch, $"/api/v1/admin/accounts/{account.Id}/status", new { status = "Frozen" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.GetAsync("/api/v1/admin/audit-logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_CanFreezeAccount_AndFrozenAccountCannotMoveMoney()
    {
        var customer = await TestUser.RegisterAsync(_http);
        var account = await customer.OpenFundedAccountAsync(100m);
        var other = await customer.OpenAccountAsync();
        var admin = await TestUser.LoginAsync(_http, "admin@walletflow.test", ApiFactory.AdminPassword);

        var freeze = await admin.SendAsync(HttpMethod.Patch, $"/api/v1/admin/accounts/{account.Id}/status", new { status = "Frozen" });
        var transfer = await customer.TransferAsync(account.Id, other.Id, 10m);

        freeze.StatusCode.Should().Be(HttpStatusCode.OK);
        transfer.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await transfer.Content.ReadAsStringAsync()).Should().Contain("account.frozen");
    }

    [Fact]
    public async Task Auditor_CanReadAuditTrail_ButCannotFreezeAccounts()
    {
        var customer = await TestUser.RegisterAsync(_http);
        var account = await customer.OpenFundedAccountAsync(100m);
        var other = await customer.OpenAccountAsync();
        (await customer.TransferAsync(account.Id, other.Id, 10m)).EnsureSuccessStatusCode();
        var auditor = await TestUser.LoginAsync(_http, "auditor@walletflow.test", ApiFactory.AuditorPassword);

        var logs = await auditor.GetAsync("/api/v1/admin/audit-logs?size=200");
        var freeze = await auditor.SendAsync(HttpMethod.Patch, $"/api/v1/admin/accounts/{account.Id}/status", new { status = "Frozen" });

        logs.StatusCode.Should().Be(HttpStatusCode.OK);
        (await logs.Content.ReadAsStringAsync()).Should().Contain("transfer.completed");
        freeze.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ForgedToken_SignedWithAnotherKey_IsRejected()
    {
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("a-completely-different-signing-key-0123456789"));
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            "walletflow", "walletflow-api",
            [new System.Security.Claims.Claim("sub", Guid.NewGuid().ToString()), new System.Security.Claims.Claim("role", "Admin")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256)));
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/audit-logs");
        request.Headers.Authorization = new("Bearer", token);

        (await _http.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
