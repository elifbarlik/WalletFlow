using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Infrastructure.Persistence;

namespace WalletFlow.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class MoneyFlowTests(ApiFactory factory)
{
    private readonly HttpClient _http = factory.CreateClient();

    // ---- transfers --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Transfer_HappyPath_MovesMoneyAndShowsInHistory()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var bob = await TestUser.RegisterAsync(_http);
        var from = await alice.OpenFundedAccountAsync(200m);
        var to = await bob.OpenAccountAsync();

        var response = await alice.TransferAsync(from.Id, to.Id, 75.25m);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await alice.GetAccountAsync(from.Id)).Balance.Should().Be(124.75m);
        (await bob.GetAccountAsync(to.Id)).Balance.Should().Be(75.25m);
        var history = await (await bob.GetAsync($"/api/v1/accounts/{to.Id}/transactions")).ReadAsync<PageResponse<TransactionResponse>>();
        history.TotalCount.Should().Be(1);
        history.Items.Single().Type.Should().Be("Transfer");
    }

    [Fact]
    public async Task Transfer_WithInsufficientFunds_Returns422AndChangesNothing()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var from = await alice.OpenFundedAccountAsync(10m);
        var to = await alice.OpenAccountAsync();

        var response = await alice.TransferAsync(from.Id, to.Id, 10.01m);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("account.insufficient_funds");
        (await alice.GetAccountAsync(from.Id)).Balance.Should().Be(10m);
    }

    [Fact]
    public async Task Transfer_WithoutIdempotencyKey_Returns400()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var a = await alice.OpenFundedAccountAsync(10m);
        var b = await alice.OpenAccountAsync();

        var response = await alice.SendAsync(HttpMethod.Post, "/api/v1/transfers",
            new { fromAccountId = a.Id, toAccountId = b.Id, amount = 1m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Transfer_ReplayedWithSameKey_MovesMoneyOnlyOnce_AndReturnsTheOriginalResult()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var from = await alice.OpenFundedAccountAsync(100m);
        var to = await alice.OpenAccountAsync();
        var key = TestUser.NewKey();

        var first = await alice.TransferAsync(from.Id, to.Id, 30m, key);
        var second = await alice.TransferAsync(from.Id, to.Id, 30m, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        (await second.ReadAsync<TransactionResponse>()).Id.Should().Be((await first.ReadAsync<TransactionResponse>()).Id);
        (await alice.GetAccountAsync(from.Id)).Balance.Should().Be(70m);
    }

    [Fact]
    public async Task Transfer_SameKeyWithDifferentBody_Returns422()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var from = await alice.OpenFundedAccountAsync(100m);
        var to = await alice.OpenAccountAsync();
        var key = TestUser.NewKey();
        (await alice.TransferAsync(from.Id, to.Id, 30m, key)).EnsureSuccessStatusCode();

        var response = await alice.TransferAsync(from.Id, to.Id, 31m, key);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("idempotency.key_reused");
    }

    [Fact]
    public async Task Transfer_TwentySimultaneousRequestsWithOneKey_ProduceExactlyOneTransfer()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var from = await alice.OpenFundedAccountAsync(1000m);
        var to = await alice.OpenAccountAsync();
        var key = TestUser.NewKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => alice.TransferAsync(from.Id, to.Id, 10m, key)));

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.Conflict);
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.Created);
        (await alice.GetAccountAsync(from.Id)).Balance.Should().Be(990m);
        (await alice.GetAccountAsync(to.Id)).Balance.Should().Be(10m);
    }

    // ---- consistency under concurrency -------------------------------------------------------------------------

    [Fact]
    public async Task Transfer_TwentyConcurrentTransfersBetweenTwoAccounts_KeepBalancesAndLedgerConsistent()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var a = await alice.OpenFundedAccountAsync(1000m);
        var b = await alice.OpenFundedAccountAsync(1000m);

        // 10 transfers A->B of 10.00 and 10 transfers B->A of 5.00, all at once, each with its own key.
        var tasks = Enumerable.Range(0, 10).Select(_ => alice.TransferAsync(a.Id, b.Id, 10m))
            .Concat(Enumerable.Range(0, 10).Select(_ => alice.TransferAsync(b.Id, a.Id, 5m)));
        var responses = await Task.WhenAll(tasks);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        (await alice.GetAccountAsync(a.Id)).Balance.Should().Be(1000m - 100m + 50m);
        (await alice.GetAccountAsync(b.Id)).Balance.Should().Be(1000m + 100m - 50m);

        // Double-entry invariant: every account's balance equals the sum of its signed ledger entries.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var id in new[] { a.Id, b.Id })
        {
            var ledger = (await db.LedgerEntries.Where(e => e.AccountId == id).ToListAsync()).Sum(e => e.SignedAmount);
            var balance = await db.Accounts.Where(x => x.Id == id).Select(x => x.Balance).SingleAsync();
            balance.Should().Be(ledger);
        }
    }

    [Fact]
    public async Task Transfer_ConcurrentOverdraw_NeverMakesTheBalanceNegative()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var from = await alice.OpenFundedAccountAsync(100m);
        var to = await alice.OpenAccountAsync();

        // 20 x 10.00 requested against a 100.00 balance, all in parallel.
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => alice.TransferAsync(from.Id, to.Id, 10m)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(10);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
        (await alice.GetAccountAsync(from.Id)).Balance.Should().Be(0m);
        (await alice.GetAccountAsync(to.Id)).Balance.Should().Be(100m);
    }

    // ---- top-ups via the external provider ---------------------------------------------------------------------

    [Fact]
    public async Task TopUp_Approved_CreditsAccountExactlyOnce_EvenWhenRetried()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var account = await alice.OpenAccountAsync();
        var key = TestUser.NewKey();
        var callsBefore = factory.Payments.Calls;

        var first = await alice.PostAsync($"/api/v1/accounts/{account.Id}/topups", new { amount = 50m, sourceToken = "tok_visa" }, key);
        var retry = await alice.PostAsync($"/api/v1/accounts/{account.Id}/topups", new { amount = 50m, sourceToken = "tok_visa" }, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        (await alice.GetAccountAsync(account.Id)).Balance.Should().Be(50m);
        (factory.Payments.Calls - callsBefore).Should().Be(1, "the provider must be charged once");
    }

    [Fact]
    public async Task TopUp_Declined_Returns402_RecordsFailedTransaction_AndKeepsBalance()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var account = await alice.OpenAccountAsync();

        var response = await alice.PostAsync($"/api/v1/accounts/{account.Id}/topups", new { amount = 50m, sourceToken = "tok_declined" }, TestUser.NewKey());

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await alice.GetAccountAsync(account.Id)).Balance.Should().Be(0m);
        var history = await (await alice.GetAsync($"/api/v1/accounts/{account.Id}/transactions")).ReadAsync<PageResponse<TransactionResponse>>();
        history.Items.Should().ContainSingle().Which.Status.Should().Be("Failed");
    }

    [Fact]
    public async Task TopUp_WhenProviderIsDown_Returns503WithRetryAfter_AndKeepsBalance()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var account = await alice.OpenAccountAsync();

        var response = await alice.PostAsync($"/api/v1/accounts/{account.Id}/topups", new { amount = 50m, sourceToken = "tok_down" }, TestUser.NewKey());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.Contains("Retry-After").Should().BeTrue();
        (await alice.GetAccountAsync(account.Id)).Balance.Should().Be(0m);
    }

    [Fact]
    public async Task TopUp_ReplayOfADeclinedKey_ReplaysTheDecline_WithoutCallingTheProviderAgain()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var account = await alice.OpenAccountAsync();
        var key = TestUser.NewKey();
        var body = new { amount = 5m, sourceToken = "tok_declined" };
        (await alice.PostAsync($"/api/v1/accounts/{account.Id}/topups", body, key)).StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        var callsAfterFirst = factory.Payments.Calls;

        var replay = await alice.PostAsync($"/api/v1/accounts/{account.Id}/topups", body, key);

        replay.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        factory.Payments.Calls.Should().Be(callsAfterFirst);
        (await alice.GetAccountAsync(account.Id)).Balance.Should().Be(0m);
    }
    // ---- outbox -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Transfer_WritesOutboxEvent_ThatTheDispatcherDelivers()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var from = await alice.OpenFundedAccountAsync(50m);
        var to = await alice.OpenAccountAsync();
        var transaction = await (await alice.TransferAsync(from.Id, to.Id, 5m)).ReadAsync<TransactionResponse>();

        var delivered = false;
        for (var i = 0; i < 40 && !delivered; i++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            delivered = await db.OutboxMessages.AnyAsync(m => m.Type == "TransferCompleted"
                && m.Payload.Contains(transaction.Id.ToString()) && m.ProcessedAt != null);
            if (!delivered)
            {
                await Task.Delay(500);
            }
        }

        delivered.Should().BeTrue("the outbox dispatcher should publish committed events within a few seconds");
    }

    [Fact]
    public async Task Validation_RejectsBadAmounts_WithFieldLevelErrors()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var a = await alice.OpenFundedAccountAsync(10m);
        var b = await alice.OpenAccountAsync();

        var response = await alice.TransferAsync(a.Id, b.Id, 1.234m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Amount");
    }
}

