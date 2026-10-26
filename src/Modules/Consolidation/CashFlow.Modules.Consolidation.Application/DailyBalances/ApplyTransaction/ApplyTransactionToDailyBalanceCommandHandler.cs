using CashFlow.Common.Application.Abstractions;
using CashFlow.Common.Application.Messaging;
using CashFlow.Common.Domain;
using CashFlow.Modules.Consolidation.Application.Abstractions.Projections;
using CashFlow.Modules.Transactions.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace CashFlow.Modules.Consolidation.Application.DailyBalances.ApplyTransaction;

internal sealed class ApplyTransactionToDailyBalanceCommandHandler(
    IDailyBalanceProjection projection,
    IDateTimeProvider dateTimeProvider,
    ILogger<ApplyTransactionToDailyBalanceCommandHandler> logger)
    : ICommandHandler<ApplyTransactionToDailyBalanceCommand>
{
    public async Task<Result> Handle(ApplyTransactionToDailyBalanceCommand command, CancellationToken cancellationToken)
    {
        bool isCredit = command.Type == ApplyTransactionToDailyBalanceCommand.Credit;

        bool applied = await projection.ApplyAsync(
            command.EventId,
            TransactionCreatedIntegrationEvent.MessageType,
            command.BusinessDate,
            credit: isCredit ? command.Amount : 0m,
            debit: isCredit ? 0m : command.Amount,
            dateTimeProvider.UtcNow,
            cancellationToken);

        // A duplicate is not a failure: it is the case the inbox exists for.
        // Success means the broker gets its ACK and stops redelivering.
        if (!applied)
        {
            logger.LogInformation("Event {EventId} was already applied; ignoring the redelivery", command.EventId);
        }

        return Result.Success();
    }
}
