using System.Diagnostics.Metrics;

namespace CashFlow.Common.Infrastructure.Messaging;

/// <summary>
/// What the consumer sees of failures. The broker does the dead-lettering past
/// the delivery limit; the consumer counts the return that crosses it.
/// </summary>
public sealed class MessagingMetrics
{
    public const string MeterName = "CashFlow.Messaging";

    private readonly Counter<long> _handlerFailures;
    private readonly Counter<long> _deadLettered;

    public MessagingMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(MeterName);

        _handlerFailures = meter.CreateCounter<long>(
            "cashflow.messaging.handler_failures", "{delivery}", "Deliveries whose handler threw; returned to the queue after a backoff.");
        _deadLettered = meter.CreateCounter<long>(
            "cashflow.messaging.dead_lettered", "{message}", "Messages sent to the dead-letter queue, by reason.");
    }

    public void HandlerFailed(string queue) =>
        _handlerFailures.Add(1, new KeyValuePair<string, object?>("queue", queue));

    public void DeadLettered(string queue, string reason) => _deadLettered.Add(
        1, new KeyValuePair<string, object?>("queue", queue), new KeyValuePair<string, object?>("reason", reason));
}
