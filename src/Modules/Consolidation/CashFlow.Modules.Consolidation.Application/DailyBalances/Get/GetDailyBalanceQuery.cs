using CashFlow.Common.Application.Messaging;

namespace CashFlow.Modules.Consolidation.Application.DailyBalances.Get;

public sealed record GetDailyBalanceQuery(DateOnly Date) : IQuery<DailyBalanceResponse>;

public sealed record DailyBalanceResponse(
    DateOnly Date,
    decimal Credits,
    decimal Debits,
    decimal Balance,
    DateTime? LastUpdatedAt,
    string Consistency)
{
    /// <summary>The balance follows the transactions asynchronously; the response says so.</summary>
    public const string Eventual = "eventual";
}
