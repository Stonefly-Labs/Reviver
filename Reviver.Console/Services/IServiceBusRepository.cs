using StoneFlyLabs.Reviver.Console.Models;

namespace StoneFlyLabs.Reviver.Console.Services;

public interface IServiceBusRepository : IAsyncDisposable
{
    string NamespaceFqdn { get; }

    Task<List<EntityInfo>> GetEntitiesWithDlqMessagesAsync(CancellationToken ct = default);
    Task<List<EntityInfo>> GetAllSendDestinationsAsync(CancellationToken ct = default);

    /// <summary>All queues and topic/subscription pairs, regardless of DLQ count (used by seeder).</summary>
    Task<List<EntityInfo>> GetAllEntitiesAsync(CancellationToken ct = default);

    Task<IDlqSession> OpenDlqSessionAsync(EntityInfo entity, int maxMessages = 20, CancellationToken ct = default);
    Task SendMessageAsync(string destination, DlqMessage message, CancellationToken ct = default);

    Task SeedDlqAsync(
        EntityInfo entity,
        int count,
        string payloadTemplate,
        string dlqReason,
        IProgress<int>? sendProgress,
        IProgress<int>? dlqProgress,
        CancellationToken ct = default);
}

public interface IDlqSession : IAsyncDisposable
{
    EntityInfo Entity { get; }
    IReadOnlyList<DlqMessage> Messages { get; }
    Task CompleteAsync(DlqMessage msg, CancellationToken ct = default);
    Task AbandonAsync(DlqMessage msg, CancellationToken ct = default);
    Task RenewLockAsync(DlqMessage msg, CancellationToken ct = default);
}
