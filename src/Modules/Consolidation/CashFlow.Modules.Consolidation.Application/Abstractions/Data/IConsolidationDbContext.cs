using CashFlow.Modules.Consolidation.Domain.DailyBalances;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Modules.Consolidation.Application.Abstractions.Data;

/// <summary>
/// The module's own view of its tables, and the only one its handlers get. It
/// names the <c>consolidation</c> schema and nothing else, so a query into
/// Transactions' tables cannot be written against it - the daily balance is
/// built from events, never by reading the transactions.
/// </summary>
/// <remarks>
/// Read side only. Balances are written by <see cref="Projections.IDailyBalanceProjection"/>.
/// </remarks>
public interface IConsolidationDbContext
{
    DbSet<DailyBalance> DailyBalances { get; }
}
