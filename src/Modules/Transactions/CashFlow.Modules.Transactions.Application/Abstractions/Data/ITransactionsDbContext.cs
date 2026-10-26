using CashFlow.Common.Application.Outbox;
using CashFlow.Modules.Transactions.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Modules.Transactions.Application.Abstractions.Data;

/// <summary>
/// The module's own view of its tables, and the only one its handlers get. It
/// names the <c>transactions</c> schema and nothing else, so a query into
/// Consolidation's tables cannot be written against it.
/// </summary>
/// <remarks>
/// Also the type <c>Hosting.Modules.ApplicationAssemblies</c> names to find
/// this assembly.
/// </remarks>
public interface ITransactionsDbContext
{
    DbSet<Transaction> Transactions { get; }

    /// <summary>
    /// Written by the same <c>SaveChangesAsync</c> as the transaction it
    /// describes, and drained by the Worker's outbox publisher.
    /// </summary>
    DbSet<OutboxMessage> OutboxMessages { get; }
}
