using Einvoice.Domain;
using Xunit;

namespace Einvoice.Tests.Unit;

public class OutboxMessageTests
{
    [Fact]
    public void Create_SetsPendingState()
    {
        var msg = OutboxMessage.Create("einvoice.invoice.created.v1", "{\"id\":\"1\"}");

        Assert.Null(msg.PublishedAtUtc);
        Assert.Equal(0, msg.Attempts);
        Assert.NotEqual(default, msg.OccurredAtUtc);
    }

    [Fact]
    public void MarkPublished_SetsTimestamp()
    {
        var msg = OutboxMessage.Create("t", "{}");
        var now = DateTimeOffset.UtcNow;

        msg.MarkPublished(now);

        Assert.Equal(now, msg.PublishedAtUtc);
    }

    [Fact]
    public void IncrementAttempt_BumpsCounter()
    {
        var msg = OutboxMessage.Create("t", "{}");
        msg.IncrementAttempt();
        msg.IncrementAttempt();
        Assert.Equal(2, msg.Attempts);
    }
}
