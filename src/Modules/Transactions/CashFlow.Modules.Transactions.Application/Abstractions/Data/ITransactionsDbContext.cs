namespace CashFlow.Modules.Transactions.Application.Abstractions.Data;

/// <summary>
/// The module's own view of its tables, and the only one its handlers get. It
/// names the <c>transactions</c> schema and nothing else, so a query into
/// Consolidation's tables cannot be written against it.
/// </summary>
/// <remarks>
/// Also the type <c>Hosting.Modules.ApplicationAssemblies</c> names to find
/// this assembly. DbSets are added with each aggregate.
/// </remarks>
public interface ITransactionsDbContext;
