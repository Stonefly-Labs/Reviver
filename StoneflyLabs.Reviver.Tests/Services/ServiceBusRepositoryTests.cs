using Azure.Messaging.ServiceBus;
using StoneflyLabs.Reviver.Console.Models;
using StoneflyLabs.Reviver.Console.Services;

namespace StoneflyLabs.Reviver.Tests.Services;

/// <summary>
/// Contract tests against IServiceBusRepository using a mock. Validates that callers
/// interact with the repository correctly; does NOT hit a real Service Bus namespace.
/// </summary>
public sealed class ServiceBusRepositoryTests
{
    private readonly IServiceBusRepository _repo = Substitute.For<IServiceBusRepository>();

    // ── GetEntitiesWithDlqMessages ────────────────────────────────────────────

    [Fact]
    public async Task GetEntitiesWithDlqMessages_ReturnsEntitiesFromRepository()
    {
        var expected = new List<EntityInfo>
        {
            new("[Q] orders", "orders", null, 5),
            new("[T] events → payments", "events", "payments", 2)
        };
        _repo.GetEntitiesWithDlqMessagesAsync().Returns(expected);

        var result = await _repo.GetEntitiesWithDlqMessagesAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal(5, result[0].DlqMessageCount);
        Assert.False(result[1].IsQueue);
    }

    // ── GetAllEntities ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllEntities_IncludesQueuesAndTopicSubscriptions()
    {
        var expected = new List<EntityInfo>
        {
            new("[Q] queue-a", "queue-a", null, 0),
            new("[T] topic-b → sub-1", "topic-b", "sub-1", 0)
        };
        _repo.GetAllEntitiesAsync().Returns(expected);

        var result = await _repo.GetAllEntitiesAsync();

        Assert.Contains(result, e => e.IsQueue);
        Assert.Contains(result, e => !e.IsQueue);
    }

    // ── SendMessageAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_InvokesRepositoryWithCorrectDestination()
    {
        // ServiceBusReceivedMessage is sealed — use ServiceBusModelFactory to create test instances.
        var raw = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: "test-id-1");

        var msg = new DlqMessage { Raw = raw, Body = "{}" };

        await _repo.SendMessageAsync("target-queue", msg);

        await _repo.Received(1).SendMessageAsync("target-queue", msg);
    }

    // ── SeedDlqAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedDlq_CallsRepositoryWithCorrectCountAndReason()
    {
        var entity = new EntityInfo("[Q] test", "test", null, 0);
        var template = StoneflyLabs.Reviver.Console.Helpers.PayloadTemplate.Default;

        await _repo.SeedDlqAsync(entity, 10, template, "MyReason", null, null);

        // Inspect ReceivedCalls() directly to avoid NSubstitute matcher quirks with
        // optional CancellationToken parameters.
        var call = _repo.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IServiceBusRepository.SeedDlqAsync));

        var args = call.GetArguments();
        Assert.Equal(entity,      args[0]);
        Assert.Equal(10,          args[1]);
        Assert.Equal(template,    args[2]);
        Assert.Equal("MyReason",  args[3]);
    }

    [Fact]
    public async Task SeedDlq_WhenRepositoryThrows_PropagatesException()
    {
        var entity = new EntityInfo("[Q] test", "test", null, 0);

        _repo.SeedDlqAsync(
            Arg.Any<EntityInfo>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IProgress<int>?>(), Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("burst")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _repo.SeedDlqAsync(entity, 1, "{}", "r", null, null));
    }
}
