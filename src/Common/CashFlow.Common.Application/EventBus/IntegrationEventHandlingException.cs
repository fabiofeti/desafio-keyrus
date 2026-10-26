using CashFlow.Common.Domain;

namespace CashFlow.Common.Application.EventBus;

/// <summary>
/// Thrown by an integration event handler whose command failed, so the broker
/// consumer negatively acknowledges the message and it is redelivered. A
/// handler that only logged the failure would let the consumer acknowledge a
/// fact that was never applied.
/// </summary>
public sealed class IntegrationEventHandlingException(Guid eventId, Error error)
    : Exception($"Integration event {eventId} was not processed: {error.Code}")
{
    public Guid EventId { get; } = eventId;

    public Error Error { get; } = error;
}
