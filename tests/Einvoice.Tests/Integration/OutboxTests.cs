using Einvoice.Domain;
using Einvoice.Infrastructure.Outbox;
using Einvoice.Infrastructure.Persistence;
using Einvoice.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RabbitMQ.Client;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Einvoice.Tests.Integration;

[Collection("containers")]
public class OutboxTests
{
    private readonly ContainersFixture _containers;

    public OutboxTests(ContainersFixture containers) => _containers = containers;

    private async Task<string> CreateIsolatedDatabaseAsync()
    {
        var admin = new NpgsqlConnectionStringBuilder(_containers.Postgres.GetConnectionString())
        {
            Database = "postgres"
        };
        await using var conn = new NpgsqlConnection(admin.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE DATABASE outbox_test";
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateDatabase)
        {
        }

        var isolated = new NpgsqlConnectionStringBuilder(_containers.Postgres.GetConnectionString())
        {
            Database = "outbox_test"
        };
        return isolated.ConnectionString;
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<EinvoiceDbContext>(options => options.UseNpgsql(connectionString));
        return services.BuildServiceProvider();
    }

    private static OutboxDispatcher CreateDispatcher(IServiceProvider provider, ICloudEventPublisher publisher)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Outbox:IntervalMs"] = "1000" })
            .Build();
        return new OutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            configuration,
            NullLogger<OutboxDispatcher>.Instance);
    }

    private static async Task<EinvoiceDbContext> CreateDbContextAsync(IServiceProvider provider)
    {
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EinvoiceDbContext>();
        await db.Database.MigrateAsync();
        return db;
    }

    private static async Task CleanOutboxAsync(IServiceProvider provider)
    {
        await using var db = await CreateDbContextAsync(provider);
        await db.OutboxMessages.ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Dispatcher_PublishesPendingMessages_AndMarksThemPublished()
    {
        var connectionString = await CreateIsolatedDatabaseAsync();
        await using var provider = BuildProvider(connectionString);
        var publisher = new RecordingPublisher();
        using var dispatcher = CreateDispatcher(provider, publisher);

        await CleanOutboxAsync(provider);
        var first = OutboxMessage.Create("einvoice.invoice.created.v1", "{\"invoiceId\":\"" + Guid.NewGuid() + "\"}");
        var second = OutboxMessage.Create("einvoice.invoice.created.v1", "{\"invoiceId\":\"" + Guid.NewGuid() + "\"}");
        await using (var seed = await CreateDbContextAsync(provider))
        {
            seed.OutboxMessages.AddRange(first, second);
            await seed.SaveChangesAsync();
        }

        var published = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        var seededPayloads = new HashSet<string> { first.Payload, second.Payload };
        var publishedSeededPayloads = publisher.Published
            .Select(p => p.Payload)
            .Where(seededPayloads.Contains)
            .ToHashSet();
        Assert.Equal(2, published);
        Assert.True(seededPayloads.SetEquals(publishedSeededPayloads),
            $"Expected published payloads [{string.Join(", ", seededPayloads)}] but got [{string.Join(", ", publishedSeededPayloads)}]");

        var seededIds = new HashSet<Guid> { first.Id, second.Id };
        var publishedSeededIds = publisher.Published.Select(p => p.MessageId).Where(seededIds.Contains).ToHashSet();
        Assert.True(seededIds.SetEquals(publishedSeededIds),
            $"Expected published ids [{string.Join(", ", seededIds)}] but got [{string.Join(", ", publishedSeededIds)}]");

        await using var verify = await CreateDbContextAsync(provider);
        var stored = await verify.OutboxMessages.Where(o => o.Id == first.Id || o.Id == second.Id).ToListAsync();
        Assert.All(stored, m => Assert.NotNull(m.PublishedAtUtc));
    }

    [Fact]
    public async Task Dispatcher_WhenPublisherFails_IncrementsAttemptAndKeepsMessagePending()
    {
        var connectionString = await CreateIsolatedDatabaseAsync();
        await using var provider = BuildProvider(connectionString);
        using var dispatcher = CreateDispatcher(provider, new FailingPublisher());

        await CleanOutboxAsync(provider);
        var message = OutboxMessage.Create("einvoice.invoice.created.v1", "{\"invoiceId\":\"" + Guid.NewGuid() + "\"}");
        await using (var seed = await CreateDbContextAsync(provider))
        {
            seed.OutboxMessages.Add(message);
            await seed.SaveChangesAsync();
        }

        var published = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        Assert.Equal(0, published);

        await using var verify = await CreateDbContextAsync(provider);
        var stored = await verify.OutboxMessages.SingleAsync(o => o.Id == message.Id);
        Assert.Null(stored.PublishedAtUtc);
        Assert.Equal(1, stored.Attempts);
    }

    [Fact]
    public async Task Dispatcher_PublishesInvoiceCreatedEventToRabbitMq()
    {
        var factory = new ConnectionFactory
        {
            HostName = _containers.RabbitMq.Hostname,
            Port = _containers.RabbitMq.GetMappedPublicPort(5672),
            UserName = "rabbitmq",
            Password = "rabbitmq"
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync("einvoice.events", ExchangeType.Topic, durable: true);
        var queue = await channel.QueueDeclareAsync($"test-einvoice-events-{Guid.NewGuid()}", durable: false,
            exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue.QueueName, "einvoice.events", "#");

        await using var apiFactory = new EinvoiceApiFactory();
        var client = apiFactory.CreateAuthedClient("invoice:write invoice:read");
        var sellerResp = await client.PostAsJsonAsync("/v1/parties",
            new { name = "S", country = "PT", vatNumber = (string?)null, type = "Supplier" });
        var buyerResp = await client.PostAsJsonAsync("/v1/parties",
            new { name = "B", country = "ES", vatNumber = (string?)null, type = "Customer" });
        var s = JsonDocument.Parse(await sellerResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        var b = JsonDocument.Parse(await buyerResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/invoices")
        {
            Content = new StringContent(
                $"{{\"number\":\"INV-OB-{Guid.NewGuid():N}\",\"issueDate\":\"2026-10-10\",\"dueDate\":\"2026-11-09\",\"currency\":\"EUR\",\"sellerId\":\"{s}\",\"buyerId\":\"{b}\",\"lines\":[{{\"description\":\"X\",\"quantity\":1,\"unitPrice\":10,\"vatRate\":0.23}}]}}",
                Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var created = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var invoiceId = createdBody.RootElement.GetProperty("id").GetGuid();

        string? contentType = null;
        JsonDocument? matched = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (matched is null && DateTime.UtcNow < deadline)
        {
            var result = await channel.BasicGetAsync(queue.QueueName, autoAck: true);
            if (result is null)
            {
                await Task.Delay(250);
                continue;
            }

            var candidate = Encoding.UTF8.GetString(result.Body.ToArray());
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                if (doc.RootElement.TryGetProperty("data", out var candidateData)
                    && candidateData.ValueKind == JsonValueKind.Object
                    && candidateData.TryGetProperty("invoiceId", out var candidateId)
                    && candidateId.GetGuid() == invoiceId)
                {
                    matched = JsonDocument.Parse(candidate);
                    contentType = result.BasicProperties.ContentType?.ToString();
                }
            }
            catch (JsonException)
            {
                // Unrelated or malformed message: keep polling until the deadline.
            }
        }

        Assert.True(matched is not null,
            $"Expected invoice.created CloudEvent for {invoiceId} within 10s.");
        Assert.NotNull(contentType);
        Assert.Contains("application/cloudevents+json", contentType);

        using (matched!)
        {
            var root = matched!.RootElement;
            var body = root.GetRawText();
            Assert.Contains("einvoice.invoice.created.v1", body);
            Assert.Contains("invoiceId", body);
            Assert.Equal("1.0", root.GetProperty("specversion").GetString());
            Assert.Equal("einvoice.invoice.created.v1", root.GetProperty("type").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("id").GetString()));
            Assert.Equal("urn:einvoice-api", root.GetProperty("source").GetString());
            var data = root.GetProperty("data");
            Assert.Equal(JsonValueKind.Object, data.ValueKind);
            Assert.Equal(invoiceId, data.GetProperty("invoiceId").GetGuid());

            var options = new DbContextOptionsBuilder<EinvoiceDbContext>()
                .UseNpgsql(_containers.Postgres.GetConnectionString())
                .Options;
            await using var db = new EinvoiceDbContext(options);
            var outboxRow = await db.OutboxMessages
                .SingleAsync(o => o.Payload.Contains(invoiceId.ToString()));
            Assert.Equal(outboxRow.Id.ToString(), root.GetProperty("id").GetString());
        }
    }

    private sealed class RecordingPublisher : ICloudEventPublisher
    {
        public bool IsConnected => true;

        public List<(string Type, string Payload, Guid MessageId)> Published { get; } = [];

        public Task PublishAsync(string type, string payloadJson, Guid messageId, CancellationToken cancellationToken = default)
        {
            Published.Add((type, payloadJson, messageId));
            return Task.CompletedTask;
        }
    }

    private sealed class FailingPublisher : ICloudEventPublisher
    {
        public bool IsConnected => true;

        public Task PublishAsync(string type, string payloadJson, Guid messageId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("broker unavailable");
    }
}
