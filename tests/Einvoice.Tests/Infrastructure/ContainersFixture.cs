using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Testcontainers.RabbitMq;
using Xunit;

namespace Einvoice.Tests.Infrastructure;

[CollectionDefinition("containers")]
public class ContainersCollection : ICollectionFixture<ContainersFixture> { }

public sealed class ContainersFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public RedisContainer Redis { get; } = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder()
        .WithImage("rabbitmq:4-management-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), Redis.StartAsync(), RabbitMq.StartAsync());
        EinvoiceApiFactory.Containers = this;
    }

    public async Task DisposeAsync() =>
        await Task.WhenAll(Postgres.DisposeAsync().AsTask(), Redis.DisposeAsync().AsTask(), RabbitMq.DisposeAsync().AsTask());
}
