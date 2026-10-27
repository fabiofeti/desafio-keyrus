using CashFlow.Common.Application.EventBus;
using CashFlow.Common.Application.Messaging;
using CashFlow.Common.Domain;
using CashFlow.Modules.Consolidation.Application.DailyBalances.ApplyTransaction;
using CashFlow.Modules.Transactions.IntegrationEvents;

namespace CashFlow.Modules.Consolidation.Presentation.IntegrationEvents;

/// <summary>
/// The way in from the broker, like an endpoint: translate the contract into
/// this module's command and report the outcome to the consumer.
/// </summary>
internal sealed class TransactionCreatedIntegrationEventHandler(
    ICommandHandler<ApplyTransactionToDailyBalanceCommand> handler)
    : IIntegrationEventHandler<TransactionCreatedIntegrationEvent>
{
    public async Task Handle(TransactionCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        Result result = await handler.Handle(
            new ApplyTransactionToDailyBalanceCommand(
                integrationEvent.Id,
                integrationEvent.BusinessDate,
                integrationEvent.Type,
                integrationEvent.Amount,
                integrationEvent.OccurredOnUtc),
            cancellationToken);

        // The failure must reach the broker: throwing means NACK and redelivery,
        // returning means ACK. Logging and returning would acknowledge a fact
        // that was never applied.
        if (result.IsFailure)
        {
            throw new IntegrationEventHandlingException(integrationEvent.Id, result.Error);
        }
    }
}
