using CashFlow.Common.Application.Messaging;

namespace CashFlow.Modules.Consolidation.Application.DailyBalances.ApplyTransaction;

/// <summary>
/// Adds one transaction to its day's totals. <c>Type</c> is the contract's
/// string ("Credit" | "Debit"): this module never sees Transactions' enum.
/// </summary>
public sealed record ApplyTransactionToDailyBalanceCommand(
    Guid EventId,
    DateOnly BusinessDate,
    string Type,
    decimal Amount) : ICommand
{
    public const string Credit = "Credit";
    public const string Debit = "Debit";
}
