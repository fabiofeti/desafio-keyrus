namespace CashFlow.Common.Application.EventBus;

/// <summary>
/// Reacts to another module's integration event. Lives in the consuming
/// module's Presentation layer: hearing a neighbour is a way in from outside,
/// like an endpoint, and turns the message into a command of its own.
/// </summary>
/// <remarks>
/// The return contract is what the broker consumer acts on: returning means the
/// effect is committed and the message is acknowledged; throwing means it was
/// not processed and the message is negatively acknowledged for redelivery.
/// Handlers must therefore be idempotent - redelivery is a normal event.
/// </remarks>
public interface IIntegrationEventHandler<in TIntegrationEvent>
    where TIntegrationEvent : IIntegrationEvent
{
    Task Handle(TIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
