namespace Einvoice.Infrastructure.Outbox;

public interface ICloudEventPublisher
{
    Task PublishAsync(string type, string payloadJson, CancellationToken cancellationToken = default);
}
