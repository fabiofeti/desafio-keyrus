using CashFlow.Common.Application.Abstractions;
using CashFlow.Common.Application.Messaging;
using CashFlow.Common.Application.Outbox;
using CashFlow.Common.Domain;
using CashFlow.Modules.Transactions.Application.Abstractions.Data;
using CashFlow.Modules.Transactions.Application.Abstractions.Telemetry;
using CashFlow.Modules.Transactions.Application.Abstractions.Time;
using CashFlow.Modules.Transactions.Domain.Transactions;
using CashFlow.Modules.Transactions.IntegrationEvents;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Modules.Transactions.Application.Transactions.Create;

internal sealed class CreateTransactionCommandHandler(
    ITransactionsDbContext context,
    ITransactionsUnitOfWork unitOfWork,
    IBusinessCalendar businessCalendar,
    ICorrelationIdProvider correlationIdProvider,
    IDateTimeProvider dateTimeProvider,
    TransactionsMetrics metrics)
    : ICommandHandler<CreateTransactionCommand, CreatedTransaction>
{
    public async Task<Result<CreatedTransaction>> Handle(
        CreateTransactionCommand command,
        CancellationToken cancellationToken)
    {
        string fingerprint = RequestFingerprint.Of(command);

        Result<CreatedTransaction>? replay = await TryReplayAsync(command, fingerprint, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        Result<Transaction> created = Transaction.Create(
            Enum.Parse<TransactionType>(command.Type),
            command.Amount,
            command.OccurredAt,
            businessCalendar.BusinessDateOf(command.OccurredAt),
            command.Description,
            command.IdempotencyKey,
            fingerprint,
            dateTimeProvider.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<CreatedTransaction>(created.Error);
        }

        Transaction transaction = created.Value;
        string correlationId = correlationIdProvider.CorrelationId;

        // Transaction and outbox row in one SaveChanges, so one local
        // transaction: if the process dies after the commit the event is still
        // in the outbox; if it dies before, neither exists. No third state.
        TransactionCreatedIntegrationEvent integrationEvent =
            transaction.ToCreatedEvent(correlationId, dateTimeProvider.UtcNow);

        context.Transactions.Add(transaction);
        context.OutboxMessages.Add(OutboxMessage.From(
            integrationEvent,
            TransactionCreatedIntegrationEvent.MessageType,
            correlationId));

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // Two requests with the same key at the same time, and the other
            // won. Not a retry: re-read and apply the same replay/conflict rule.
            return await TryReplayAsync(command, fingerprint, cancellationToken)
                ?? throw new InvalidOperationException("Unique violation without a matching idempotency key.");
        }

        metrics.TransactionCreated(command.Type);
        return new CreatedTransaction(transaction.ToResponse(), Replayed: false);
    }

    private async Task<Result<CreatedTransaction>?> TryReplayAsync(
        CreateTransactionCommand command, string fingerprint, CancellationToken cancellationToken)
    {
        Transaction? existing = await context.Transactions
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.IdempotencyKey == command.IdempotencyKey, cancellationToken);

        if (existing is null)
        {
            return null;
        }

        if (existing.RequestFingerprint != fingerprint)
        {
            return Result.Failure<CreatedTransaction>(TransactionErrors.IdempotencyKeyReused(command.IdempotencyKey));
        }

        metrics.RequestReplayed();
        return new CreatedTransaction(existing.ToResponse(), Replayed: true);
    }
}
