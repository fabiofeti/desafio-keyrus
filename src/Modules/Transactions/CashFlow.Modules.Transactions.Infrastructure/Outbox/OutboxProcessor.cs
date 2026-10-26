using CashFlow.Common.Application.Abstractions;
using CashFlow.Common.Application.Outbox;
using CashFlow.Common.Infrastructure.Messaging;
using CashFlow.Modules.Transactions.Infrastructure.Database;
using CashFlow.Modules.Transactions.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace CashFlow.Modules.Transactions.Infrastructure.Outbox;

/// <summary>
/// One publishing cycle: lock a batch of unpublished rows, publish them in
/// order, mark each one only after the broker confirmed it, commit.
/// </summary>
/// <remarks>
/// <para>
/// <c>FOR UPDATE SKIP LOCKED</c> lets two Worker instances run side by side
/// without publishing the same row twice in parallel.
/// </para>
/// <para>
/// The guarantee is at-least-once: if the Worker dies between a confirm and the
/// commit, those rows are published again on the next cycle, and the consumer's
/// inbox absorbs the duplicate. The row locks are held while publishing; the
/// publish pipeline's per-attempt timeout bounds that to seconds even when the
/// broker is unreachable.
/// </para>
/// </remarks>
internal sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IRabbitMqPublisher publisher,
    IDateTimeProvider dateTimeProvider,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger)
{
    public Task DeclareTopologyAsync(CancellationToken cancellationToken) =>
        publisher.DeclareExchangeAsync(TransactionCreatedIntegrationEvent.Exchange, cancellationToken);

    /// <returns>What the cycle did, so the loop knows whether to pause.</returns>
    public async Task<OutboxCycle> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        TransactionsDbContext context = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // batchSize is configuration, and FromSql sends it as a parameter.
        int batchSize = options.Value.BatchSize;
        List<OutboxMessage> batch = await context.OutboxMessages
            .FromSql($"""
                SELECT * FROM transactions.outbox_messages
                WHERE published_on_utc IS NULL
                ORDER BY occurred_on_utc
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        int published = 0;
        bool failed = false;

        foreach (OutboxMessage message in batch)
        {
            try
            {
                await publisher.PublishAsync(ToOutgoing(message), cancellationToken);
            }
            catch (BrokenCircuitException)
            {
                // The breaker is protecting a broker that is already failing; this
                // was not an attempt at this message, so it is not counted as one.
                logger.LogWarning("Outbox publishing paused: the broker circuit is open");
                failed = true;
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                message.RecordFailure(exception.Message);
                logger.LogWarning(
                    exception,
                    "Publishing outbox message {MessageId} failed (attempt {Attempt}); it stays unpublished",
                    message.Id, message.AttemptCount);
                failed = true;

                // Stop at the first failure, so messages keep their order.
                break;
            }

            // Only now: the broker has confirmed it.
            message.MarkPublished(dateTimeProvider.UtcNow);
            published++;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (published > 0)
        {
            logger.LogInformation("Published {Count} outbox message(s)", published);
        }

        return new OutboxCycle(batch.Count, published, failed);
    }

    private static OutgoingMessage ToOutgoing(OutboxMessage message) => new(
        TransactionCreatedIntegrationEvent.Exchange,
        RoutingKeyOf(message.Type),
        message.Id,
        message.Type,
        message.CorrelationId,
        System.Text.Encoding.UTF8.GetBytes(message.Payload));

    // One event type today. A second one gets its routing key here, from its own contract.
    private static string RoutingKeyOf(string messageType) => messageType switch
    {
        TransactionCreatedIntegrationEvent.MessageType => TransactionCreatedIntegrationEvent.RoutingKey,
        _ => throw new InvalidOperationException($"No route for outbox message type '{messageType}'.")
    };
}

internal readonly record struct OutboxCycle(int Locked, int Published, bool Failed);
