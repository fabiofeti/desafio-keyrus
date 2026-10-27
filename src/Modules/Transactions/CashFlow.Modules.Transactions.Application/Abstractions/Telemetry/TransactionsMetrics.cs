using System.Diagnostics.Metrics;

namespace CashFlow.Modules.Transactions.Application.Abstractions.Telemetry;

/// <summary>
/// The module's instruments, on its own meter. Plain System.Diagnostics.Metrics:
/// readable with <c>dotnet-counters</c> today, exportable by OpenTelemetry
/// later without touching the call sites.
/// </summary>
public sealed class TransactionsMetrics
{
    public const string MeterName = "CashFlow.Transactions";

    private readonly Counter<long> _created;
    private readonly Counter<long> _replayed;
    private readonly Counter<long> _outboxPublished;
    private readonly Counter<long> _outboxPublishFailures;
    private readonly Counter<long> _outboxCircuitOpen;

    public TransactionsMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(MeterName);

        _created = meter.CreateCounter<long>(
            "cashflow.transactions.created", "{transaction}", "Transactions committed together with their outbox message.");
        _replayed = meter.CreateCounter<long>(
            "cashflow.transactions.replayed", "{request}", "POSTs answered from an earlier request with the same Idempotency-Key.");
        _outboxPublished = meter.CreateCounter<long>(
            "cashflow.outbox.published", "{message}", "Outbox messages marked published after the broker confirmed them.");
        _outboxPublishFailures = meter.CreateCounter<long>(
            "cashflow.outbox.publish_failures", "{attempt}", "Publish attempts that failed; the message stays in the outbox.");
        _outboxCircuitOpen = meter.CreateCounter<long>(
            "cashflow.outbox.circuit_open", "{cycle}", "Publishing cycles skipped because the broker circuit was open.");
    }

    public void TransactionCreated(string type) => _created.Add(1, new KeyValuePair<string, object?>("type", type));

    public void RequestReplayed() => _replayed.Add(1);

    public void OutboxPublished(int count) => _outboxPublished.Add(count);

    public void OutboxPublishFailed() => _outboxPublishFailures.Add(1);

    public void OutboxCircuitOpen() => _outboxCircuitOpen.Add(1);
}
