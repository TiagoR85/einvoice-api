using System.Text.Json;
using Einvoice.Domain;
using Einvoice.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Einvoice.Infrastructure.Inbox;

public sealed class InvoiceCompletedConsumer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<InvoiceCompletedConsumer> logger) : BackgroundService
{
    private const string Queue = "einvoice-api.invoice-completed";
    private const string RetryQueue = Queue + ".retry";
    private const string DeadLetterQueue = Queue + ".dlq";
    private const string Exchange = "einvoice.events";
    private const string RoutingKey = "einvoice.invoice.completed.v1";
    private const string AttemptHeader = "x-attempt";
    private const int MaxRetries = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = configuration["RabbitMq:Host"] ?? "localhost",
            Port = int.TryParse(configuration["RabbitMq:Port"], out var port) ? port : 5672,
            UserName = configuration["RabbitMq:Username"] ?? "guest",
            Password = configuration["RabbitMq:Password"] ?? "guest",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };

        IConnection? connection = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                connection = await factory.CreateConnectionAsync(stoppingToken);
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "RabbitMQ not reachable yet; retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        if (connection is null)
            return;

        var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await DeclareTopologyAsync(channel, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) => await OnReceivedAsync(channel, ea, stoppingToken);
        await channel.BasicConsumeAsync(Queue, autoAck: false, consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation("Inbox consuming {Queue}", Queue);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private static async Task DeclareTopologyAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(RetryQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = Exchange,
                ["x-dead-letter-routing-key"] = RoutingKey
            }, cancellationToken: ct);
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, RoutingKey, cancellationToken: ct);
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var attempt = ResolveAttempt(ea.BasicProperties.Headers);
        try
        {
            var invoiceId = ParseInvoiceId(ea.Body);
            if (invoiceId is null)
            {
                logger.LogWarning("Poison completed event (no invoiceId) — parking in DLQ");
                await ParkAsync(channel, ea, DeadLetterQueue, ct);
                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EinvoiceDbContext>();
            var invoice = await db.Invoices.FindAsync([invoiceId.Value], ct);

            if (invoice is null)
            {
                logger.LogInformation("Completed event for unknown invoice {InvoiceId}; acking", invoiceId);
            }
            else if (invoice.Status == InvoiceStatus.Issued)
            {
                invoice.MarkProcessing();
                invoice.MarkCompleted();
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Invoice {InvoiceId} marked Completed via inbox", invoiceId);
            }
            else if (invoice.Status == InvoiceStatus.Processing)
            {
                invoice.MarkCompleted();
                await db.SaveChangesAsync(ct);
            }
            // Completed/Failed/Draft: idempotent skip

            await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Inbox processing failed (attempt {Attempt})", attempt);
            try
            {
                if (attempt >= MaxRetries)
                {
                    await ParkAsync(channel, ea, DeadLetterQueue, ct);
                }
                else
                {
                    var delay = TimeSpan.FromMilliseconds(Math.Min(15000 * Math.Pow(2, attempt), 300000));
                    var headers = ea.BasicProperties.Headers is null
                        ? new Dictionary<string, object?>()
                        : new Dictionary<string, object?>(ea.BasicProperties.Headers);
                    headers[AttemptHeader] = attempt + 1;
                    var props = new BasicProperties
                    {
                        DeliveryMode = DeliveryModes.Persistent,
                        Expiration = ((long)delay.TotalMilliseconds).ToString(),
                        Headers = headers
                    };
                    await channel.BasicPublishAsync(string.Empty, RetryQueue, false, props, ea.Body, ct);
                }

                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            }
            catch (Exception routeEx)
            {
                logger.LogError(routeEx, "Failed to route failed delivery {Tag}", ea.DeliveryTag);
                await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true, CancellationToken.None);
            }
        }
    }

    private static async Task ParkAsync(IChannel channel, BasicDeliverEventArgs ea, string queue, CancellationToken ct)
    {
        var props = new BasicProperties { DeliveryMode = DeliveryModes.Persistent };
        await channel.BasicPublishAsync(string.Empty, queue, false, props, ea.Body, ct);
    }

    private static int ResolveAttempt(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(AttemptHeader, out var raw))
            return 0;
        return raw switch
        {
            int i => i,
            long l => (int)l,
            string s => int.TryParse(s, out var parsed) ? parsed : 0,
            byte[] b => int.TryParse(System.Text.Encoding.UTF8.GetString(b), out var parsed) ? parsed : 0,
            _ => 0
        };
    }

    private static Guid? ParseInvoiceId(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return null;
            if (!data.TryGetProperty("invoiceId", out var idEl))
                return null;
            return Guid.TryParse(idEl.GetString(), out var id) && id != Guid.Empty ? id : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
