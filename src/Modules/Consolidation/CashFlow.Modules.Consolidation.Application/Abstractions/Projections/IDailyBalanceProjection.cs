namespace CashFlow.Modules.Consolidation.Application.Abstractions.Projections;

/// <summary>
/// Records an event in the inbox and adds its amounts to the day's totals, as
/// one atomic operation. A port, because the guarantee lives in the SQL - one
/// statement, so there is no state in which the event is recorded but not
/// added, or added but not recorded.
/// </summary>
public interface IDailyBalanceProjection
{
    /// <returns><c>false</c> when the event had already been applied; nothing changed.</returns>
    Task<bool> ApplyAsync(
        Guid eventId,
        string messageType,
        DateOnly businessDate,
        decimal credit,
        decimal debit,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}
