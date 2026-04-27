using StoneFlyLabs.Reviver.Console.Models;

namespace StoneFlyLabs.Reviver.Tests.Services;

public sealed class EntityInfoTests
{
    [Fact]
    public void Queue_IsQueue_ReturnsTrue()
    {
        var e = new EntityInfo("[Q] orders", "orders", null, 3);
        Assert.True(e.IsQueue);
    }

    [Fact]
    public void TopicSubscription_IsQueue_ReturnsFalse()
    {
        var e = new EntityInfo("[T] events → sub", "events", "sub", 1);
        Assert.False(e.IsQueue);
    }

    [Fact]
    public void Queue_SendPath_IsQueueName()
    {
        var e = new EntityInfo("[Q] orders", "orders", null, 0);
        Assert.Equal("orders", e.SendPath);
    }

    [Fact]
    public void TopicSubscription_SendPath_IsTopicName()
    {
        // Sending goes to the topic; the subscription receives via its own filter.
        var e = new EntityInfo("[T] events → sub", "events", "sub", 0);
        Assert.Equal("events", e.SendPath);
    }
}
