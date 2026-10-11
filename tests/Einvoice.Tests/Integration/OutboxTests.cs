using Einvoice.Domain;
using Einvoice.Infrastructure.Outbox;
using Einvoice.Infrastructure.Persistence;
using Einvoice.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
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

    [Fact]
    public async Task Dispatcher_PublishesPendingMessages_AndMarksThemPublished()
    {
        var connectionString = await CreateIsolatedDatabaseAsync();
        await using var provider = BuildProvider(connectionString);
        var publisher = new RecordingPublisher();
        using var dispatcher = CreateDispatcher(provider, publisher);

        var first = OutboxMessage.Create("einvoice.invoice.created.v1", "{\"invoiceId\":\"" + Guid.NewGuid() + "\"}");
        var second = OutboxMessage.Create("einvoice.invoice.created.v1", "{\"invoiceId\":\"" + Guid.NewGuid() + "\"}");
        await using (var seed = await CreateDbContextAsync(provider))
        {
            seed.OutboxMessages.AddRange(first, second);
            await seed.SaveChangesAsync();
        }

        var published = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        Assert.Equal(2, published);
        Assert.Equal([first.Id, second.Id], publisher.Published.Select(m => m.Id).ToArray());

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

    private sealed class RecordingPublisher : ICloudEventPublisher
    {
        public List<OutboxMessage> Published { get; } = [];

        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingPublisher : ICloudEventPublisher
    {
        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("broker unavailable");
    }
}
