using System.Diagnostics.Metrics;

namespace CashFlow.Modules.Consolidation.Application.Abstractions.Telemetry;

/// <summary>
/// The module's instruments, on its own meter. The lag histogram is the
/// number behind "eventual": how long after the transaction was recorded its
/// amount reached the daily balance.
/// </summary>
public sealed class ConsolidationMetrics
{
    public const string MeterName = "CashFlow.Consolidation";

    private readonly Counter<long> _applied;
    private readonly Counter<long> _duplicates;
    private readonly Histogram<double> _lag;

    public ConsolidationMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(MeterName);

        _applied = meter.CreateCounter<long>(
            "cashflow.consolidation.events_applied", "{event}", "Events whose amount was added to the daily balance.");
        _duplicates = meter.CreateCounter<long>(
            "cashflow.consolidation.events_duplicate", "{event}", "Redeliveries the inbox recognised and ignored.");
        _lag = meter.CreateHistogram<double>(
            "cashflow.consolidation.lag", "s", "Time from the transaction's outbox event to its effect on the daily balance.");
    }

    public void EventApplied(TimeSpan lag)
    {
        _applied.Add(1);
        _lag.Record(Math.Max(0, lag.TotalSeconds));
    }

    public void DuplicateIgnored() => _duplicates.Add(1);
}
