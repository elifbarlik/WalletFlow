using System.Net;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using WalletFlow.Infrastructure.Redis;

namespace WalletFlow.Api.IntegrationTests;

/// <summary>Runs the API against real Redis (idempotency store + distributed rate limiting) and real PostgreSQL.</summary>
[Collection(RedisApiCollection.Name)]
public sealed class RedisTests(RedisApiFactory factory)
{
    private readonly HttpClient _http = factory.CreateClient();

    private IConnectionMultiplexer Redis => factory.Services.GetRequiredService<IConnectionMultiplexer>();

    private async Task WipeIdempotencyKeysAsync()
    {
        var db = Redis.GetDatabase();
        foreach (var server in Redis.GetServers())
        {
            await foreach (var key in server.KeysAsync(pattern: "idem:*"))
            {
                await db.KeyDeleteAsync(key);
            }
        }
    }

    [Fact]
    public async Task Health_Ready_ReportsRedis()
    {
        (await _http.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Redis.GetDatabase().PingAsync()).Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Transfer_ReplayedWithSameKey_MovesMoneyOnce_AndKeysExpire()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var from = await alice.OpenFundedAccountAsync(100m);
        var to = await alice.OpenAccountAsync();
        var key = TestUser.NewKey();

        var first = await alice.TransferAsync(from.Id, to.Id, 30m, key);
        var second = await alice.TransferAsync(from.Id, to.Id, 30m, key);

        (await second.ReadAsync<TransactionResponse>()).Id.Should().Be((await first.ReadAsync<TransactionResponse>()).Id);
        (await alice.GetAccountAsync(from.Id)).Balance.Should().Be(70m);
        var stored = Redis.GetServers().SelectMany(s => s.Keys(pattern: $"idem:*:{key}")).Single();
        (await Redis.GetDatabase().KeyTimeToLiveAsync(stored)).Should().BeGreaterThan(TimeSpan.FromHours(23));
    }

    [Fact]
    public async Task Transfer_TwentySimultaneousRequestsWithOneKey_ProduceExactlyOneTransfer()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var from = await alice.OpenFundedAccountAsync(1000m);
        var to = await alice.OpenAccountAsync();
        var key = TestUser.NewKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => alice.TransferAsync(from.Id, to.Id, 10m, key)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict);
        (await alice.GetAccountAsync(from.Id)).Balance.Should().Be(990m);
        (await alice.GetAccountAsync(to.Id)).Balance.Should().Be(10m);
    }

    [Fact]
    public async Task Transfer_WhenRedisLostEveryKey_ThePrimaryKeyStillPreventsADoubleTransfer()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var from = await alice.OpenFundedAccountAsync(100m);
        var to = await alice.OpenAccountAsync();
        var key = TestUser.NewKey();
        var first = await alice.TransferAsync(from.Id, to.Id, 30m, key);

        await WipeIdempotencyKeysAsync(); // simulates a Redis flush / failover with data loss
        var replay = await alice.TransferAsync(from.Id, to.Id, 30m, key);

        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        (await replay.ReadAsync<TransactionResponse>()).Id.Should().Be((await first.ReadAsync<TransactionResponse>()).Id);
        (await alice.GetAccountAsync(from.Id)).Balance.Should().Be(70m);
        (await alice.TransferAsync(from.Id, to.Id, 31m, key)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task TopUp_WhenRedisLostEveryKey_IsNotChargedTwice()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var account = await alice.OpenAccountAsync();
        var key = TestUser.NewKey();
        var body = new { amount = 40m, sourceToken = "tok_visa" };
        (await alice.PostAsync($"/api/v1/accounts/{account.Id}/topups", body, key)).EnsureSuccessStatusCode();
        var callsAfterFirst = factory.Payments.Calls;

        await WipeIdempotencyKeysAsync();
        var replay = await alice.PostAsync($"/api/v1/accounts/{account.Id}/topups", body, key);

        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.Payments.Calls.Should().Be(callsAfterFirst, "a completed top-up is never sent to the provider again");
        (await alice.GetAccountAsync(account.Id)).Balance.Should().Be(40m);
    }

    [Fact]
    public async Task TopUp_AfterProviderOutage_CanBeRetriedWithTheSameKey()
    {
        var alice = await TestUser.RegisterAsync(_http);
        var account = await alice.OpenAccountAsync();
        var key = TestUser.NewKey();
        (await alice.PostAsync($"/api/v1/accounts/{account.Id}/topups", new { amount = 5m, sourceToken = "tok_down" }, key))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // The fake provider only fails for tok_down; the amount must match, the token may differ for the retry.
        var retry = await alice.PostAsync($"/api/v1/accounts/{account.Id}/topups", new { amount = 5m, sourceToken = "tok_visa" }, key);

        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        (await alice.GetAccountAsync(account.Id)).Balance.Should().Be(5m);
    }

    [Fact]
    public async Task RateLimiter_SharesItsCounterAcrossInstances()
    {
        var limiters = factory.Services.GetRequiredService<IDistributedRateLimiterFactory>();
        var key = $"test:{Guid.NewGuid():N}";
        using var replicaA = limiters.CreateFixedWindow(key, 3, TimeSpan.FromMinutes(1));
        using var replicaB = limiters.CreateFixedWindow(key, 3, TimeSpan.FromMinutes(1));

        var leases = new List<RateLimitLease>();
        foreach (var limiter in new[] { replicaA, replicaB, replicaA, replicaB, replicaA })
        {
            leases.Add(await limiter.AcquireAsync());
        }

        leases.Count(l => l.IsAcquired).Should().Be(3, "two replicas must not each get their own quota");
        leases.Last().TryGetMetadata(MetadataName.RetryAfter.Name, out _).Should().BeTrue();
    }
}
