using CashFlow.Common.Application.Messaging;

namespace CashFlow.Modules.Consolidation.Application.DailyBalances.ApplyTransaction;

/// <summary>
/// Adds one transaction to its day's totals. <c>Type</c> is the contract's
/// string ("Credit" | "Debit"): this module never sees Transactions' enum.
/// <c>EventOccurredOnUtc</c> is when the event was written to the outbox; it
/// only feeds the consolidation lag metric.
/// </summary>
public sealed record ApplyTransactionToDailyBalanceCommand(
    Guid EventId,
    DateOnly BusinessDate,
    string Type,
    decimal Amount,
    DateTime EventOccurredOnUtc) : ICommand
{
    public const string Credit = "Credit";
    public const string Debit = "Debit";
}
