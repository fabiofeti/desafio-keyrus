using CashFlow.Common.Application.Abstractions;

namespace CashFlow.Common.Infrastructure.Time;

internal sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
