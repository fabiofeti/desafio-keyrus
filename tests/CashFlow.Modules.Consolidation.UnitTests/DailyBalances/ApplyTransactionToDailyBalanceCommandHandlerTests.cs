using CashFlow.Common.Application.Abstractions;
using CashFlow.Common.Domain;
using CashFlow.Modules.Consolidation.Application.Abstractions.Projections;
using CashFlow.Modules.Consolidation.Application.Abstractions.Telemetry;
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
    private static readonly DateTime EventAtUtc = NowUtc.AddSeconds(-1.5);

    private readonly IDailyBalanceProjection _projection = Substitute.For<IDailyBalanceProjection>();
    private readonly TestMeterFactory _meters = new();

    [Fact]
    public async Task Handle_Should_AddTheAmountAsCredit_WhenTypeIsCredit()
    {
        // Arrange
        var command = new ApplyTransactionToDailyBalanceCommand(Guid.NewGuid(), Date, "Credit", 125.50m, EventAtUtc);
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
        var command = new ApplyTransactionToDailyBalanceCommand(Guid.NewGuid(), Date, "Debit", 20m, EventAtUtc);
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
            new ApplyTransactionToDailyBalanceCommand(Guid.NewGuid(), Date, "Credit", 1m, EventAtUtc), default);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_Should_RecordTheLag_WhenTheEventIsApplied()
    {
        // Arrange
        _projection.ApplyAsync(default, default!, default, default, default, default, default).ReturnsForAnyArgs(true);
        var command = new ApplyTransactionToDailyBalanceCommand(Guid.NewGuid(), Date, "Credit", 5m, EventAtUtc);

        // Act
        var lag = await _meters.MeasureAsync("cashflow.consolidation.lag", () => CreateHandler().Handle(command, default));

        // Assert: from the outbox event to now, in seconds.
        lag.Single().Value.ShouldBe((NowUtc - EventAtUtc).TotalSeconds);
    }

    [Fact]
    public async Task Handle_Should_CountTheDuplicate_AndRecordNoLag_WhenTheEventWasAlreadyApplied()
    {
        // Arrange
        _projection.ApplyAsync(default, default!, default, default, default, default, default).ReturnsForAnyArgs(false);
        var command = new ApplyTransactionToDailyBalanceCommand(Guid.NewGuid(), Date, "Credit", 5m, EventAtUtc);
        ApplyTransactionToDailyBalanceCommandHandler handler = CreateHandler();

        // Act
        var lag = new List<(double, KeyValuePair<string, object?>[])>();
        var duplicates = await _meters.MeasureAsync("cashflow.consolidation.events_duplicate", async () =>
            lag = await _meters.MeasureAsync("cashflow.consolidation.lag", () => handler.Handle(command, default)));

        // Assert
        duplicates.Count.ShouldBe(1);
        lag.ShouldBeEmpty();
    }

    private ApplyTransactionToDailyBalanceCommandHandler CreateHandler()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(NowUtc);

        return new ApplyTransactionToDailyBalanceCommandHandler(
            _projection, clock, new ConsolidationMetrics(_meters), NullLogger<ApplyTransactionToDailyBalanceCommandHandler>.Instance);
    }
}
