using CashFlow.Modules.Consolidation.Application.Abstractions.Projections;
using CashFlow.Modules.Consolidation.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Modules.Consolidation.Infrastructure.DailyBalances;

/// <summary>
/// Inbox and balance in one statement, so in one transaction:
/// <list type="bullet">
/// <item>a redelivered event hits the inbox's primary key, the CTE returns no
/// row, and nothing is added;</item>
/// <item>two deliveries of the same event at once: the second waits on the
/// first's key and falls into DO NOTHING;</item>
/// <item>two different events for the same day at once: the increment is done
/// by the database (credits = credits + x), so neither is lost.</item>
/// </list>
/// Never retried by the application: redelivery by the broker is the retry,
/// and it is safe precisely because of the inbox.
/// </summary>
internal sealed class DailyBalanceProjection(ConsolidationDbContext context) : IDailyBalanceProjection
{
    public async Task<bool> ApplyAsync(
        Guid eventId,
        string messageType,
        DateOnly businessDate,
        decimal credit,
        decimal debit,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        int rows = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH inbox AS (
                INSERT INTO consolidation.inbox_messages (event_id, message_type, processed_at_utc)
                VALUES ({eventId}, {messageType}, {nowUtc})
                ON CONFLICT (event_id) DO NOTHING
                RETURNING event_id
            )
            INSERT INTO consolidation.daily_balances (business_date, credits, debits, updated_at_utc)
            SELECT {businessDate}, {credit}, {debit}, {nowUtc}
            FROM inbox
            ON CONFLICT (business_date) DO UPDATE
            SET credits        = daily_balances.credits + EXCLUDED.credits,
                debits         = daily_balances.debits + EXCLUDED.debits,
                updated_at_utc = EXCLUDED.updated_at_utc
            """,
            cancellationToken);

        return rows > 0;
    }
}
