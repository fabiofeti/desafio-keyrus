using CashFlow.Common.Application.Messaging;
using CashFlow.Common.Domain;
using CashFlow.Modules.Consolidation.Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Modules.Consolidation.Application.DailyBalances.Get;

internal sealed class GetDailyBalanceQueryHandler(IConsolidationDbContext context)
    : IQueryHandler<GetDailyBalanceQuery, DailyBalanceResponse>
{
    public async Task<Result<DailyBalanceResponse>> Handle(
        GetDailyBalanceQuery query,
        CancellationToken cancellationToken)
    {
        // Only this module's table: the balance is what the events built, never a
        // sum over Transactions' rows.
        DailyBalanceResponse? balance = await context.DailyBalances
            .AsNoTracking()
            .Where(b => b.BusinessDate == query.Date)
            .Select(b => new DailyBalanceResponse(
                b.BusinessDate, b.Credits, b.Debits, b.Credits - b.Debits, b.UpdatedAtUtc, DailyBalanceResponse.Eventual))
            .SingleOrDefaultAsync(cancellationToken);

        // A day with nothing consolidated is a zero balance, not a 404: the client
        // need not tell "no movement" from "not processed yet", and a null
        // lastUpdatedAt already says nothing was consolidated for the date.
        return balance ?? new DailyBalanceResponse(query.Date, 0m, 0m, 0m, null, DailyBalanceResponse.Eventual);
    }
}
