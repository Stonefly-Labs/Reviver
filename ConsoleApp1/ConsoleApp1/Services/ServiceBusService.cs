using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using SbDlq.Models;

namespace SbDlq.Services;

public sealed class ServiceBusService : IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusAdministrationClient _adminClient;

    public string NamespaceFqdn { get; }

    public ServiceBusService(string namespaceFqdn)
    {
        NamespaceFqdn = namespaceFqdn;
        var credential = new AzureCliCredential();
        _client = new ServiceBusClient(namespaceFqdn, credential);
        _adminClient = new ServiceBusAdministrationClient(namespaceFqdn, credential);
    }

    public async Task<List<EntityInfo>> GetEntitiesWithDlqMessagesAsync(CancellationToken ct = default)
    {
        var result = new List<EntityInfo>();

        await foreach (var props in _adminClient.GetQueuesRuntimePropertiesAsync(ct))
        {
            if (props.DeadLetterMessageCount > 0)
                result.Add(new EntityInfo(
                    $"[Q] {props.Name}",
                    props.Name,
                    null,
                    props.DeadLetterMessageCount));
        }

        await foreach (var topic in _adminClient.GetTopicsAsync(ct))
        {
            await foreach (var sub in _adminClient.GetSubscriptionsRuntimePropertiesAsync(topic.Name, ct))
            {
                if (sub.DeadLetterMessageCount > 0)
                    result.Add(new EntityInfo(
                        $"[T] {topic.Name} → {sub.SubscriptionName}",
                        topic.Name,
                        sub.SubscriptionName,
                        sub.DeadLetterMessageCount));
            }
        }

        return result;
    }

    public async Task<List<EntityInfo>> GetAllSendDestinationsAsync(CancellationToken ct = default)
    {
        var result = new List<EntityInfo>();

        await foreach (var queue in _adminClient.GetQueuesAsync(ct))
            result.Add(new EntityInfo($"[Q] {queue.Name}", queue.Name, null, 0));

        await foreach (var topic in _adminClient.GetTopicsAsync(ct))
            result.Add(new EntityInfo($"[T] {topic.Name}", topic.Name, null, 0));

        return result;
    }

    public async Task<DlqSession> OpenDlqSessionAsync(EntityInfo entity, int maxMessages = 20, CancellationToken ct = default)
    {
        var receiver = entity.IsQueue
            ? _client.CreateReceiver(entity.QueueOrTopicName,
                new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter })
            : _client.CreateReceiver(entity.QueueOrTopicName, entity.SubscriptionName!,
                new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        var rawMessages = await receiver.ReceiveMessagesAsync(maxMessages, TimeSpan.FromSeconds(5), ct);

        var messages = rawMessages.Select(m => new DlqMessage
        {
            Raw = m,
            Body = m.Body?.ToString() ?? string.Empty,
            ApplicationProperties = m.ApplicationProperties.ToDictionary(k => k.Key, k => k.Value)
        }).ToList();

        return new DlqSession(entity, receiver, messages);
    }

    public async Task SendMessageAsync(string destination, DlqMessage message, CancellationToken ct = default)
    {
        await using var sender = _client.CreateSender(destination);

        var outMsg = new ServiceBusMessage(BinaryData.FromString(message.Body));

        if (message.ContentType is not null)   outMsg.ContentType      = message.ContentType;
        if (message.CorrelationId is not null) outMsg.CorrelationId    = message.CorrelationId;
        if (message.Subject is not null)       outMsg.Subject          = message.Subject;
        if (message.Raw.To is not null)        outMsg.To               = message.Raw.To;
        if (message.Raw.ReplyTo is not null)   outMsg.ReplyTo          = message.Raw.ReplyTo;
        if (message.Raw.SessionId is not null) outMsg.SessionId        = message.Raw.SessionId;

        foreach (var (key, value) in message.ApplicationProperties)
            outMsg.ApplicationProperties[key] = value;

        await sender.SendMessageAsync(outMsg, ct);
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync();
}

// Represents a live receive session against one DLQ — holds the locked messages and the receiver.
public sealed class DlqSession : IAsyncDisposable
{
    private readonly ServiceBusReceiver _receiver;

    public EntityInfo Entity { get; }
    public List<DlqMessage> Messages { get; }

    internal DlqSession(EntityInfo entity, ServiceBusReceiver receiver, List<DlqMessage> messages)
    {
        Entity = entity;
        _receiver = receiver;
        Messages = messages;
    }

    public Task CompleteAsync(DlqMessage msg, CancellationToken ct = default)
        => _receiver.CompleteMessageAsync(msg.Raw, ct);

    public Task AbandonAsync(DlqMessage msg, CancellationToken ct = default)
        => _receiver.AbandonMessageAsync(msg.Raw, cancellationToken: ct);

    public Task RenewLockAsync(DlqMessage msg, CancellationToken ct = default)
        => _receiver.RenewMessageLockAsync(msg.Raw, ct);

    public async ValueTask DisposeAsync() => await _receiver.DisposeAsync();
}
