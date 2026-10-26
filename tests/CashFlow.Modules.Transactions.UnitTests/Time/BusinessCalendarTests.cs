using CashFlow.Modules.Transactions.Infrastructure.Time;
using Microsoft.Extensions.Options;

namespace CashFlow.Modules.Transactions.UnitTests.Time;

public class BusinessCalendarTests
{
    private readonly BusinessCalendar _calendar = new(Options.Create(new BusinessCalendarOptions()));

    [Fact]
    public void BusinessDateOf_Should_UseTheBusinessTimeZone_NotUtc()
    {
        // Arrange: 01:30 UTC on the 27th is still 22:30 on the 26th in São Paulo.
        var instant = new DateTimeOffset(2026, 10, 27, 1, 30, 0, TimeSpan.Zero);

        // Act & Assert
        _calendar.BusinessDateOf(instant).ShouldBe(new DateOnly(2026, 10, 26));
    }

    [Fact]
    public void BusinessDateOf_Should_NotDependOnTheOffsetTheClientSent()
    {
        // Arrange: the same instant, written two ways.
        var local = new DateTimeOffset(2026, 10, 26, 23, 59, 0, TimeSpan.FromHours(-3));
        var utc = local.ToUniversalTime();

        // Act & Assert
        _calendar.BusinessDateOf(local).ShouldBe(_calendar.BusinessDateOf(utc));
        _calendar.BusinessDateOf(local).ShouldBe(new DateOnly(2026, 10, 26));
    }
}
