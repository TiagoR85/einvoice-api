using Einvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Einvoice.Infrastructure.Outbox;

public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICloudEventPublisher _publisher;
    private readonly ILogger<OutboxDispatcher> _logger;
    private readonly TimeSpan _interval;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        ICloudEventPublisher publisher,
        IConfiguration configuration,
        ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _logger = logger;
        var ms = int.TryParse(configuration["Outbox:IntervalMs"], out var v) ? v : 2000;
        _interval = TimeSpan.FromMilliseconds(ms);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Outbox dispatch failed");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    internal async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EinvoiceDbContext>();

        var pending = await db.OutboxMessages
            .Where(o => o.PublishedAtUtc == null)
            .OrderBy(o => o.OccurredAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        var published = 0;
        foreach (var message in pending)
        {
            message.IncrementAttempt();
            try
            {
                await _publisher.PublishAsync(message, cancellationToken);
                message.MarkPublished(DateTimeOffset.UtcNow);
                published++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish outbox message {Id} (attempt {Attempts})",
                    message.Id, message.Attempts);
            }
        }

        if (pending.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return published;
    }
}
