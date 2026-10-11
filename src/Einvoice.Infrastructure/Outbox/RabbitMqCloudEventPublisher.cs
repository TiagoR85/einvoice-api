using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using System.Text.Json.Nodes;

namespace Einvoice.Infrastructure.Outbox;

public sealed class RabbitMqCloudEventPublisher : ICloudEventPublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly string _exchange;
    private readonly JsonEventFormatter _formatter = new();

    public RabbitMqCloudEventPublisher(IConfiguration configuration)
    {
        var rmq = configuration.GetSection("RabbitMq");
        _exchange = rmq["Exchange"] ?? "einvoice.events";

        var factory = new ConnectionFactory
        {
            HostName = rmq["Host"] ?? "localhost",
            Port = int.TryParse(rmq["Port"], out var port) ? port : 5672,
            UserName = rmq["Username"] ?? "guest",
            Password = rmq["Password"] ?? "guest"
        };

        _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
        _channel = _connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true))
            .GetAwaiter().GetResult();
        _channel.ExchangeDeclareAsync(_exchange, ExchangeType.Topic, durable: true)
            .GetAwaiter().GetResult();
    }

    public async Task PublishAsync(string type, string payloadJson, CancellationToken cancellationToken = default)
    {
        var cloudEvent = new CloudEvent
        {
            Id = Guid.NewGuid().ToString(),
            Type = type,
            Source = new Uri("urn:einvoice-api"),
            Subject = type,
            Time = DateTimeOffset.UtcNow,
            DataContentType = "application/json",
            Data = JsonNode.Parse(payloadJson)
        };

        var body = _formatter.EncodeStructuredModeMessage(cloudEvent, out var contentType);
        var properties = new BasicProperties { ContentType = contentType.ToString() };

        await _channel.BasicPublishAsync(exchange: _exchange, routingKey: type, mandatory: false,
            basicProperties: properties, body: body, cancellationToken: cancellationToken);
    }

    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }
}
