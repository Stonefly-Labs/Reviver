namespace StoneFlyLabs.Reviver.Console.Models;

public sealed record EntityInfo(
    string DisplayName,
    string QueueOrTopicName,
    string? SubscriptionName,
    long DlqMessageCount)
{
    public bool IsQueue => SubscriptionName is null;

    // For sending: queues by name, topics by topic name (the subscription receives via subscription filter)
    public string SendPath => QueueOrTopicName;

    public override string ToString() => DisplayName;
}
