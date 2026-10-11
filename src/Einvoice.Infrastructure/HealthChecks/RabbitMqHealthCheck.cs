using Einvoice.Infrastructure.Outbox;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Einvoice.Infrastructure.HealthChecks;

public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly ICloudEventPublisher _publisher;

    public RabbitMqHealthCheck(ICloudEventPublisher publisher) => _publisher = publisher;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        // publisher mantem conexao viva; construcao no DI falharia no startup se RabbitMQ estivesse fora
        return Task.FromResult(_publisher is RabbitMqCloudEventPublisher
            ? HealthCheckResult.Healthy("RabbitMQ connection held")
            : HealthCheckResult.Unhealthy("Publisher not initialized"));
    }
}
