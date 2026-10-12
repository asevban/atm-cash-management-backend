using AtmCashManagement.Api.Domain.Entities;
using AtmCashManagement.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace AtmCashManagement.Api.Application;

public interface IOutboxEventPublisher
{
    Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken);
}

public sealed class LoggingOutboxEventPublisher(ILogger<LoggingOutboxEventPublisher> logger) : IOutboxEventPublisher
{
    public Task PublishAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Operations alert {EventType} {EventId} ATM {AtmId} cassette {CassetteId}: {Payload}",
            outboxEvent.EventType,
            outboxEvent.Id,
            outboxEvent.AtmId,
            outboxEvent.CassetteId,
            outboxEvent.Payload);
        return Task.CompletedTask;
    }
}

public sealed class OutboxEventProcessor(
    AtmDbContext db,
    IOutboxEventPublisher publisher,
    IClock clock,
    ILogger<OutboxEventProcessor> logger)
{
    private const int BatchSize = 100;

    public async Task<int> ProcessPendingBatchAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var pending = await db.OutboxEvents
            .FromSqlRaw(
                "SELECT * FROM outbox_events WHERE \"Status\" = 'Pending' " +
                "ORDER BY \"CreatedAtUtc\", \"Id\" LIMIT 100 FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        var sentCount = 0;
        foreach (var item in pending)
        {
            try
            {
                await publisher.PublishAsync(item, cancellationToken);
                item.MarkSent(clock.UtcNow);
                sentCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                item.RecordFailure();
                logger.LogError(exception, "Outbox event {EventId} remains pending after a processing failure.", item.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return sentCount;
    }
}
