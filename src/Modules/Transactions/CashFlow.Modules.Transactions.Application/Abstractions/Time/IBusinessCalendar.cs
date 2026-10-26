namespace CashFlow.Modules.Transactions.Application.Abstractions.Time;

/// <summary>
/// Which day an instant counts for. The answer depends on the business time
/// zone, not on the server's, and is computed once - when the transaction is
/// recorded - and stored.
/// </summary>
public interface IBusinessCalendar
{
    DateOnly BusinessDateOf(DateTimeOffset instant);
}
