using CashFlow.Modules.Transactions.Domain.Transactions;
using CashFlow.Modules.Transactions.IntegrationEvents;

namespace CashFlow.Modules.Transactions.Application.Transactions.Create;

/// <summary>
/// Domain to the outside world. Here, not on the contract: the contract must
/// not know the Domain.
/// </summary>
internal static class TransactionMappings
{
    public static TransactionResponse ToResponse(this Transaction transaction) => new(
        transaction.Id,
        transaction.Type.ToString(),
        transaction.Amount,
        transaction.OccurredAt,
        transaction.BusinessDate,
        transaction.Description);

    public static TransactionCreatedIntegrationEvent ToCreatedEvent(
        this Transaction transaction, string correlationId, DateTime nowUtc) => new(
        Id: Guid.CreateVersion7(),
        OccurredOnUtc: nowUtc,
        TransactionId: transaction.Id,
        Type: transaction.Type.ToString(),
        Amount: transaction.Amount,
        BusinessDate: transaction.BusinessDate,
        OccurredAt: transaction.OccurredAt,
        Version: TransactionCreatedIntegrationEvent.CurrentVersion,
        CorrelationId: correlationId);
}
