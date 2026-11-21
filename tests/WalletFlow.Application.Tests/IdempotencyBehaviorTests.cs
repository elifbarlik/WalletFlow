using System.Text.Json.Serialization;
using FluentAssertions;
using MediatR;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Application.Common.Behaviors;

namespace WalletFlow.Application.Tests;

public class IdempotencyBehaviorTests
{
    private sealed record DoWork(string Value, [property: JsonIgnore] string IdempotencyKey) : IRequest<WorkResult>, IIdempotentRequest;

    private sealed record WorkResult(int Run, string Value);

    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly InMemoryStore _store = new();
    private readonly Guid _user = Guid.NewGuid();
    private int _runs;

    private IdempotencyBehavior<DoWork, WorkResult> Behavior(Guid? user = null) =>
        new(_store, new TestUser(user ?? _user), Clock);

    private RequestHandlerDelegate<WorkResult> Work(string value, Exception? failWith = null) => () =>
    {
        _runs++;
        return failWith is null ? Task.FromResult(new WorkResult(_runs, value)) : Task.FromException<WorkResult>(failWith);
    };

    [Fact]
    public async Task SameKeyAndPayload_RunsOnce_AndReplaysTheFirstResponse()
    {
        var request = new DoWork("a", "key-12345678");

        var first = await Behavior().Handle(request, Work("a"), default);
        var second = await Behavior().Handle(request, Work("a"), default);

        _runs.Should().Be(1);
        second.Should().Be(first);
    }

    [Fact]
    public async Task SameKeyDifferentPayload_IsRejected()
    {
        await Behavior().Handle(new DoWork("a", "key-12345678"), Work("a"), default);

        var act = () => Behavior().Handle(new DoWork("b", "key-12345678"), Work("b"), default);

        await act.Should().ThrowAsync<IdempotencyKeyReusedException>();
        _runs.Should().Be(1);
    }

    [Fact]
    public async Task SameKeyFromAnotherUser_IsIndependent()
    {
        await Behavior().Handle(new DoWork("a", "key-12345678"), Work("a"), default);

        await Behavior(Guid.NewGuid()).Handle(new DoWork("a", "key-12345678"), Work("a"), default);

        _runs.Should().Be(2);
    }

    [Fact]
    public async Task WhenHandlerFails_KeyIsReleased_SoTheClientMayRetry()
    {
        var request = new DoWork("a", "key-12345678");

        var failing = () => Behavior().Handle(request, Work("a", new ConflictException("x", "boom")), default);
        await failing.Should().ThrowAsync<ConflictException>();
        var retry = await Behavior().Handle(request, Work("a"), default);

        retry.Value.Should().Be("a");
        _runs.Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentDuplicate_WhileInProgress_IsRejectedWith409Semantics()
    {
        var request = new DoWork("a", "key-12345678");
        var inner = Behavior();
        Task<WorkResult>? duplicate = null;

        await inner.Handle(request, () =>
        {
            duplicate = Behavior().Handle(request, Work("a"), default);
            return Task.FromResult(new WorkResult(0, "a"));
        }, default);

        var act = () => duplicate!;
        await act.Should().ThrowAsync<RequestInProgressException>();
    }

    [Fact]
    public async Task StaleReservation_IsTakenOver()
    {
        var request = new DoWork("a", "key-12345678");
        // Simulate a crashed request: reservation exists but never completed.
        _store.Entries[(_user, "key-12345678")] = new IdempotencyEntry(nameof(DoWork), Hash(request), false, null, Clock.UtcNow);
        Clock.UtcNow = Clock.UtcNow.Add(IdempotencyBehavior<DoWork, WorkResult>.StaleAfter).AddSeconds(1);

        var result = await Behavior().Handle(request, Work("a"), default);

        result.Run.Should().Be(1);
    }

    private static string Hash(DoWork request) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(
            request, request.GetType(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))));
}

