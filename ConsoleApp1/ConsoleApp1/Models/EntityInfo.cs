namespace SbDlq.Models;

public sealed record EntityInfo(
    string DisplayName,
    string QueueOrTopicName,
    string? SubscriptionName,
    long DlqMessageCount)
{
    public bool IsQueue => SubscriptionName is null;

    // Path used to send: queues by name, topic/sub by topic name (routing selects sub)
    public string SendPath => QueueOrTopicName;

    public override string ToString() => DisplayName;
}