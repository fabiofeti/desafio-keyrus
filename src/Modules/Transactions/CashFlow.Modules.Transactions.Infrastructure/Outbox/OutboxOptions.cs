namespace CashFlow.Modules.Transactions.Infrastructure.Outbox;

internal sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Rows locked and published per cycle.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>Pause after an empty or failed cycle.</summary>
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(1);
}
