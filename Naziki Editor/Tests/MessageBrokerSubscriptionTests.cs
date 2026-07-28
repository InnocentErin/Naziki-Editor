using Naziki_Editor.Core.Messaging;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class MessageBrokerSubscriptionTests
{
    [Fact]
    public void DisposedSubscriptionIsNotInvoked()
    {
        var broker = new MessageBroker();
        var calls = 0;
        var subscription = broker.Subscribe("topic", () => calls++);
        broker.Publish("topic");
        subscription.Dispose();
        broker.Publish("topic");
        Assert.Equal(1, calls);
    }
}
