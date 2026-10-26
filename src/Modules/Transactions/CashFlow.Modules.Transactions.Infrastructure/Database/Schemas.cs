namespace CashFlow.Modules.Transactions.Infrastructure.Database;

/// <summary>
/// Must match deploy/postgres/init/01-roles.sh, which creates the schema and
/// grants <c>transactions_app</c> access to it and to nothing else.
/// </summary>
internal static class Schemas
{
    internal const string Transactions = "transactions";
}
