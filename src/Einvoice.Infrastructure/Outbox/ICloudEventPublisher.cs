namespace Einvoice.Infrastructure.Outbox;

public interface ICloudEventPublisher
{
    /// <summary>True enquanto a conexao com o broker esta aberta (RabbitMQ.Client 7.x: IConnection.IsOpen / IChannel.IsOpen).</summary>
    bool IsConnected { get; }

    Task PublishAsync(string type, string payloadJson, CancellationToken cancellationToken = default);
}
