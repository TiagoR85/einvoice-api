using Einvoice.Infrastructure.HealthChecks;
using Einvoice.Infrastructure.Outbox;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Einvoice.Tests.Unit;

public class RabbitMqHealthCheckTests
{
    [Fact]
    public async Task CheckHealth_WhenPublisherConnected_ReturnsHealthy()
    {
        var check = new RabbitMqHealthCheck(new FakePublisher { IsConnected = true });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealth_WhenPublisherDisconnected_ReturnsUnhealthyWithoutException()
    {
        var check = new RabbitMqHealthCheck(new FakePublisher { IsConnected = false });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.False(string.IsNullOrWhiteSpace(result.Description));
    }

    private sealed class FakePublisher : ICloudEventPublisher
    {
        public bool IsConnected { get; init; }

        public Task PublishAsync(string type, string payloadJson, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
