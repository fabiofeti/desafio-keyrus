using System.Text.Json;
using CashFlow.Common.Application.Abstractions;
using CashFlow.Common.Application.Outbox;
using CashFlow.Common.Domain;
using CashFlow.Modules.Transactions.Application.Abstractions.Telemetry;
using CashFlow.Modules.Transactions.Application.Abstractions.Time;
using CashFlow.Modules.Transactions.Application.Transactions.Create;
using CashFlow.Modules.Transactions.Domain.Transactions;
using CashFlow.Modules.Transactions.Infrastructure.Database;
using CashFlow.Modules.Transactions.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace CashFlow.Modules.Transactions.UnitTests.Transactions;

public class CreateTransactionCommandHandlerTests : BaseHandlerTest
{
    private static readonly DateOnly BusinessDate = new(2026, 10, 26);
    private static readonly DateTime NowUtc = new(2026, 10, 26, 13, 0, 5, DateTimeKind.Utc);

    private readonly TestMeterFactory _meters = new();

    [Fact]
    public async Task Handle_Should_PersistTransactionAndOutboxMessage_WhenValid()
    {
        // Arrange
        TransactionsDbContext context = CreateDbContext();
        CreateTransactionCommandHandler handler = CreateHandler(context);

        // Act
        Result<CreatedTransaction> result = await handler.Handle(Command(), default);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Replayed.ShouldBeFalse();
        result.Value.Transaction.BusinessDate.ShouldBe(BusinessDate);

        Transaction transaction = await context.Transactions.SingleAsync();
        transaction.Id.ShouldBe(result.Value.Transaction.Id);

        OutboxMessage message = await context.OutboxMessages.SingleAsync();
        message.Type.ShouldBe(TransactionCreatedIntegrationEvent.MessageType);
        message.CorrelationId.ShouldBe("corr-1");
        message.PublishedOnUtc.ShouldBeNull();

        TransactionCreatedIntegrationEvent payload = JsonSerializer.Deserialize<TransactionCreatedIntegrationEvent>(
            message.Payload, OutboxMessage.PayloadSerializerOptions)!;
        payload.Id.ShouldBe(message.Id);
        payload.TransactionId.ShouldBe(transaction.Id);
        payload.Type.ShouldBe("Credit");
        payload.Amount.ShouldBe(125.50m);
        payload.BusinessDate.ShouldBe(BusinessDate);
        payload.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_Should_CountTheCreatedTransaction_ByType_AndTheReplayApart()
    {
        // Arrange
        TransactionsDbContext context = CreateDbContext();
        CreateTransactionCommandHandler handler = CreateHandler(context);

        // Act
        var created = await _meters.MeasureAsync("cashflow.transactions.created", async () =>
        {
            await handler.Handle(Command(), default);
            await handler.Handle(Command(), default);
        });
        var replayed = await _meters.MeasureAsync("cashflow.transactions.replayed", () => handler.Handle(Command(), default));

        // Assert: the repeated POST is a replay, not a second transaction.
        created.Count.ShouldBe(1);
        created.Single().Tags.ShouldContain(new KeyValuePair<string, object?>("type", "Credit"));
        replayed.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_Should_ReplayTheSameTransaction_WhenRepeatedWithSameKeyAndPayload()
    {
        // Arrange
        TransactionsDbContext context = CreateDbContext();
        CreateTransactionCommandHandler handler = CreateHandler(context);
        Result<CreatedTransaction> first = await handler.Handle(Command(), default);

        // Act
        Result<CreatedTransaction> second = await handler.Handle(Command(), default);

        // Assert: same body, marked as replayed, and nothing written twice.
        second.Value.Replayed.ShouldBeTrue();
        second.Value.Transaction.ShouldBe(first.Value.Transaction);
        (await context.Transactions.CountAsync()).ShouldBe(1);
        (await context.OutboxMessages.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_Should_Replay_WhenTheSameInstantIsSentWithAnotherOffset()
    {
        // Arrange
        TransactionsDbContext context = CreateDbContext();
        CreateTransactionCommandHandler handler = CreateHandler(context);
        await handler.Handle(Command(), default);

        // Act: 10:00-03:00 and 13:00Z are the same request.
        Result<CreatedTransaction> result = await handler.Handle(
            Command() with { OccurredAt = new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.Zero) }, default);

        // Assert
        result.Value.Replayed.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_Should_ReturnConflict_WhenKeyReusedWithDifferentPayload()
    {
        // Arrange
        TransactionsDbContext context = CreateDbContext();
        CreateTransactionCommandHandler handler = CreateHandler(context);
        await handler.Handle(Command(), default);

        // Act
        Result<CreatedTransaction> result = await handler.Handle(Command() with { Amount = 11m }, default);

        // Assert
        result.Error.ShouldBe(TransactionErrors.IdempotencyKeyReused("key-1"));
        (await context.Transactions.CountAsync()).ShouldBe(1);
        (await context.OutboxMessages.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_Should_ReturnDomainError_AndWriteNothing_WhenAggregateRejects()
    {
        // Arrange: the validator would stop this first; the aggregate still must.
        TransactionsDbContext context = CreateDbContext();
        CreateTransactionCommandHandler handler = CreateHandler(context);

        // Act
        Result<CreatedTransaction> result = await handler.Handle(Command() with { Amount = 0m }, default);

        // Assert
        result.Error.ShouldBe(TransactionErrors.AmountMustBePositive);
        (await context.Transactions.CountAsync()).ShouldBe(0);
        (await context.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    private static CreateTransactionCommand Command() => new(
        "key-1", "Credit", 125.50m, new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.FromHours(-3)), "Venda");

    private CreateTransactionCommandHandler CreateHandler(TransactionsDbContext context)
    {
        IBusinessCalendar calendar = Substitute.For<IBusinessCalendar>();
        calendar.BusinessDateOf(Arg.Any<DateTimeOffset>()).Returns(BusinessDate);

        ICorrelationIdProvider correlation = Substitute.For<ICorrelationIdProvider>();
        correlation.CorrelationId.Returns("corr-1");

        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(NowUtc);

        return new CreateTransactionCommandHandler(context, context, calendar, correlation, clock, new TransactionsMetrics(_meters));
    }
}
