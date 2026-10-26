using CashFlow.Common.Domain;

namespace CashFlow.Modules.Consolidation.Domain.DailyBalances;

/// <summary>
/// The consolidated totals of one business day. Written only by the inbox's
/// atomic SQL (credits = credits + x in the database, never read-add-write in
/// the application); this type exists for reads and for the mapping.
/// </summary>
public sealed class DailyBalance : Entity
{
    private DailyBalance()
    {
    }

    public DateOnly BusinessDate { get; private set; }

    public decimal Credits { get; private set; }

    public decimal Debits { get; private set; }

    /// <summary>Derived, never stored: it cannot disagree with its parts.</summary>
    public decimal Balance => Credits - Debits;

    public DateTime UpdatedAtUtc { get; private set; }
}
