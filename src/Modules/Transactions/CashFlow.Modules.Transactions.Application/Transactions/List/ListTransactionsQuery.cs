using CashFlow.Common.Application.Messaging;
using CashFlow.Modules.Transactions.Application.Abstractions.Pagination;

namespace CashFlow.Modules.Transactions.Application.Transactions.List;

public sealed record ListTransactionsQuery(DateOnly Date, int Page, int PageSize)
    : IQuery<PagedResponse<TransactionListItem>>
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
}

public sealed record TransactionListItem(
    Guid Id,
    string Type,
    decimal Amount,
    DateTimeOffset OccurredAt,
    DateOnly BusinessDate,
    string? Description,
    DateTime CreatedAtUtc);
