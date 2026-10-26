using CashFlow.Common.Domain;

namespace CashFlow.Modules.Transactions.Application.Transactions.List;

internal static class ListTransactionsErrors
{
    public static readonly Error InvalidPage = Error.Problem(
        "Transactions.InvalidPage", "The page must be 1 or greater.");

    public static readonly Error InvalidPageSize = Error.Problem(
        "Transactions.InvalidPageSize",
        $"The page size must be between 1 and {ListTransactionsQuery.MaxPageSize}.");
}
