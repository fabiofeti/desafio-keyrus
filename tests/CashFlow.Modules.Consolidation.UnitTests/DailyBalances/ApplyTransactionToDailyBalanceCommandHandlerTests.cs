using CashFlow.Common.Application.Abstractions;
using CashFlow.Common.Domain;
using CashFlow.Modules.Consolidation.Application.Abstractions.Projections;
using CashFlow.Modules.Consolidation.Application.DailyBalances.ApplyTransaction;
using CashFlow.Modules.Transactions.IntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CashFlow.Modules.Consolidation.UnitTests.DailyBalances;

/// <summary>
/// The SQL that makes this atomic is PostgreSQL's (ON CONFLICT), so it is
/// covered by the integration tests; here, only what the handler decides.
/// </summary>
public class ApplyTransactionToDailyBalanceCommandHandlerTests
{
    private static readonly DateOnly Date = new(2026, 10, 26);
    private static readonly DateTime NowUtc = new(2026, 10, 26, 13, 0, 0, DateTimeKind.Utc);

    private readonly IDailyBalanceProjection _projection = Substitute.For<IDailyBalanceProjection>();

    [Fact]
    public async Task Handle_Should_AddTheAmountAsCredit_WhenTypeIsCredit()
    {
        // Arrange
        var command = new ApplyTransactionToDailyBalanceCommand(Guid.NewGuid(), Date, "Credit", 125.50m);
        _projection.ApplyAsync(default, default!, default, default, default, default, default).ReturnsForAnyArgs(true);

        // Act
        Result result = await CreateHandler().Handle(command, default);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        await _projection.Received(1).ApplyAsync(
            command.EventId, TransactionCreatedIntegrationEvent.MessageType, Date, 125.50m, 0m, NowUtc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_AddTheAmountAsDebit_WhenTypeIsDebit()
    {
        // Arrange
        var command = new ApplyTransactionToDailyBalanceCommand(Guid.NewGuid(), Date, "Debit", 20m);
        _projection.ApplyAsync(default, default!, default, default, default, default, default).ReturnsForAnyArgs(true);

        // Act
        await CreateHandler().Handle(command, default);

        // Assert
        await _projection.Received(1).ApplyAsync(
            command.EventId, Arg.Any<string>(), Date, 0m, 20m, NowUtc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Succeed_WhenTheEventWasAlreadyApplied()
    {
        // Arrange: a duplicate is the case the inbox exists for; success means ACK.
        _projection.ApplyAsync(default, default!, default, default, default, default, default).ReturnsForAnyArgs(false);

        // Act
        Result result = await CreateHandler().Handle(
            new ApplyTransactionToDailyBalanceCommand(Guid.NewGuid(), Date, "Credit", 1m), default);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    private ApplyTransactionToDailyBalanceCommandHandler CreateHandler()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(NowUtc);

        return new ApplyTransactionToDailyBalanceCommandHandler(
            _projection, clock, NullLogger<ApplyTransactionToDailyBalanceCommandHandler>.Instance);
    }
}
