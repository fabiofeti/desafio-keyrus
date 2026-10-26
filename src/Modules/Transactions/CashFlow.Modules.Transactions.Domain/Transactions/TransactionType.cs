namespace CashFlow.Modules.Transactions.Domain.Transactions;

/// <summary>
/// Carries the sign of a transaction; the amount itself is always positive.
/// Stored as text, so a reordering of this enum cannot rewrite history.
/// </summary>
public enum TransactionType
{
    Credit = 1,
    Debit = 2
}
