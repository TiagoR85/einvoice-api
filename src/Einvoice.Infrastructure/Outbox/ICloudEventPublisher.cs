using Einvoice.Domain;

namespace Einvoice.Infrastructure.Outbox;

public interface ICloudEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
