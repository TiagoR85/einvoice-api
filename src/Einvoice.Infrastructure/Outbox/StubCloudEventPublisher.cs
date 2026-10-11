using Einvoice.Domain;

namespace Einvoice.Infrastructure.Outbox;

public sealed class StubCloudEventPublisher : ICloudEventPublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("RabbitMqCloudEventPublisher is not wired yet.");
}
