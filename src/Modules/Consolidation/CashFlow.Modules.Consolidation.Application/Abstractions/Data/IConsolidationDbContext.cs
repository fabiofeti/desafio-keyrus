namespace CashFlow.Modules.Consolidation.Application.Abstractions.Data;

/// <summary>
/// The module's own view of its tables, and the only one its handlers get. It
/// names the <c>consolidation</c> schema and nothing else, so a query into
/// Transactions' tables cannot be written against it - the daily balance is
/// built from events, never by reading the transactions.
/// </summary>
/// <remarks>
/// Also the type <c>Hosting.Modules.ApplicationAssemblies</c> names to find
/// this assembly. DbSets are added with each aggregate.
/// </remarks>
public interface IConsolidationDbContext;
