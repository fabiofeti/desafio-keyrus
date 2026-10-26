using CashFlow.Common.Application.EventBus;
using CashFlow.Common.Application.Messaging;
using CashFlow.Common.Domain;
using CashFlow.Modules.Consolidation.Application.DailyBalances.ApplyTransaction;
using CashFlow.Modules.Consolidation.Presentation.IntegrationEvents;
using CashFlow.Modules.Transactions.IntegrationEvents;
using NSubstitute;

namespace CashFlow.Modules.Consolidation.UnitTests.IntegrationEvents;

public class TransactionCreatedIntegrationEventHandlerTests
{
    private static readonly TransactionCreatedIntegrationEvent Event = new(
        Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Credit", 10m,
        new DateOnly(2026, 10, 26), DateTimeOffset.UtcNow, 1, "corr-1");

    private readonly ICommandHandler<ApplyTransactionToDailyBalanceCommand> _inner =
        Substitute.For<ICommandHandler<ApplyTransactionToDailyBalanceCommand>>();

    [Fact]
    public async Task Handle_Should_TranslateTheEventIntoTheCommand()
    {
        // Arrange
        _inner.Handle(default!, default).ReturnsForAnyArgs(Result.Success());

        // Act
        await new TransactionCreatedIntegrationEventHandler(_inner).Handle(Event);

        // Assert
        await _inner.Received(1).Handle(
            new ApplyTransactionToDailyBalanceCommand(Event.Id, Event.BusinessDate, "Credit", 10m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Throw_WhenTheCommandFails()
    {
        // Arrange: throwing is what makes the consumer NACK instead of ACK.
        Error error = Error.Problem("Validation.General", "invalid");
        _inner.Handle(default!, default).ReturnsForAnyArgs(Result.Failure(error));

        // Act
        IntegrationEventHandlingException exception = await Should.ThrowAsync<IntegrationEventHandlingException>(
            () => new TransactionCreatedIntegrationEventHandler(_inner).Handle(Event));

        // Assert
        exception.EventId.ShouldBe(Event.Id);
        exception.Error.ShouldBe(error);
    }
}
