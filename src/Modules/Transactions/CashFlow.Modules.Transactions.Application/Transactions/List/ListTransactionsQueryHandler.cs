using CashFlow.Common.Application.Messaging;
using CashFlow.Common.Domain;
using CashFlow.Modules.Transactions.Application.Abstractions.Data;
using CashFlow.Modules.Transactions.Application.Abstractions.Pagination;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Modules.Transactions.Application.Transactions.List;

internal sealed class ListTransactionsQueryHandler(ITransactionsDbContext context)
    : IQueryHandler<ListTransactionsQuery, PagedResponse<TransactionListItem>>
{
    public async Task<Result<PagedResponse<TransactionListItem>>> Handle(
        ListTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        // Queries have no validator (the decorator validates commands only).
        if (query.Page < 1)
        {
            return Result.Failure<PagedResponse<TransactionListItem>>(ListTransactionsErrors.InvalidPage);
        }

        if (query.PageSize is < 1 or > ListTransactionsQuery.MaxPageSize)
        {
            return Result.Failure<PagedResponse<TransactionListItem>>(ListTransactionsErrors.InvalidPageSize);
        }

        IQueryable<Domain.Transactions.Transaction> ofTheDay = context.Transactions
            .AsNoTracking()
            .Where(t => t.BusinessDate == query.Date);

        int totalCount = await ofTheDay.CountAsync(cancellationToken);

        // Id breaks ties, so a page boundary is stable between requests.
        List<TransactionListItem> items = await ofTheDay
            .OrderBy(t => t.OccurredAt)
            .ThenBy(t => t.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(t => new TransactionListItem(
                t.Id, t.Type.ToString(), t.Amount, t.OccurredAt, t.BusinessDate, t.Description, t.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResponse<TransactionListItem>(items, query.Page, query.PageSize, totalCount);
    }
}
