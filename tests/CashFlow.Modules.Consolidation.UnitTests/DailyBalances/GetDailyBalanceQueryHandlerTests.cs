using CashFlow.Common.Domain;
using CashFlow.Modules.Consolidation.Application.DailyBalances.Get;
using CashFlow.Modules.Consolidation.Infrastructure.Database;

namespace CashFlow.Modules.Consolidation.UnitTests.DailyBalances;

public class GetDailyBalanceQueryHandlerTests : BaseHandlerTest
{
    private static readonly DateOnly Date = new(2026, 10, 26);
    private static readonly DateTime UpdatedAtUtc = new(2026, 10, 26, 13, 1, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_Should_ReturnTotalsAndDerivedBalance_WhenTheDayWasConsolidated()
    {
        // Arrange
        ConsolidationDbContext context = CreateDbContext();
        await SeedBalanceAsync(context, Date, 125.50m, 20.00m, UpdatedAtUtc);
        await SeedBalanceAsync(context, Date.AddDays(1), 999m, 1m, UpdatedAtUtc);

        // Act
        Result<DailyBalanceResponse> result = await new GetDailyBalanceQueryHandler(context)
            .Handle(new GetDailyBalanceQuery(Date), default);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new DailyBalanceResponse(Date, 125.50m, 20.00m, 125.50m - 20.00m, UpdatedAtUtc, "eventual"));
    }

    [Fact]
    public async Task Handle_Should_ReturnZerosAndNoUpdate_WhenNothingWasConsolidatedForTheDay()
    {
        // Arrange
        ConsolidationDbContext context = CreateDbContext();
        await SeedBalanceAsync(context, Date.AddDays(-1), 10m, 0m, UpdatedAtUtc);

        // Act
        Result<DailyBalanceResponse> result = await new GetDailyBalanceQueryHandler(context)
            .Handle(new GetDailyBalanceQuery(Date), default);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new DailyBalanceResponse(Date, 0m, 0m, 0m, null, "eventual"));
    }

    [Fact]
    public async Task Handle_Should_ReturnNegativeBalance_WhenDebitsExceedCredits()
    {
        // Arrange
        ConsolidationDbContext context = CreateDbContext();
        await SeedBalanceAsync(context, Date, 10m, 30.25m, UpdatedAtUtc);

        // Act
        Result<DailyBalanceResponse> result = await new GetDailyBalanceQueryHandler(context)
            .Handle(new GetDailyBalanceQuery(Date), default);

        // Assert
        result.Value.Balance.ShouldBe(10m - 30.25m);
    }
}
