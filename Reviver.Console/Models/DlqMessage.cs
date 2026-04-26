using Azure.Messaging.ServiceBus;

namespace StoneFlyLabs.Reviver.Models;

public sealed class DlqMessage
{
    public required ServiceBusReceivedMessage Raw { get; init; }
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, object> ApplicationProperties { get; set; } = [];

    public string MessageId => Raw.MessageId;
    public string? ContentType => Raw.ContentType;
    public string? Subject => Raw.Subject;
    public string? CorrelationId => Raw.CorrelationId;
    public string? DeadLetterReason => Raw.DeadLetterReason;
    public string? DeadLetterErrorDescription => Raw.DeadLetterErrorDescription;
    public DateTimeOffset EnqueuedAt => Raw.EnqueuedTime;
    public DateTimeOffset LockedUntil => Raw.LockedUntil;
}
