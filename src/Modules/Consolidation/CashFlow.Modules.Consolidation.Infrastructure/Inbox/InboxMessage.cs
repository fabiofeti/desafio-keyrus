namespace CashFlow.Modules.Consolidation.Infrastructure.Inbox;

/// <summary>
/// An event this module has already applied. Plumbing, not domain: it exists so
/// a redelivered event is recognised and applied zero more times. Rows are
/// written only by <see cref="DailyBalances.DailyBalanceProjection"/>, in the
/// same statement as the balance they protect.
/// </summary>
internal sealed class InboxMessage
{
    private InboxMessage()
    {
    }

    public Guid EventId { get; private set; }

    public string MessageType { get; private set; } = string.Empty;

    public DateTime ProcessedAtUtc { get; private set; }
}
