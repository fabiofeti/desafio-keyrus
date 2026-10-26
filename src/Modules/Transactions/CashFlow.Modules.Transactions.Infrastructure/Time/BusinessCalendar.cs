using CashFlow.Modules.Transactions.Application.Abstractions.Time;
using Microsoft.Extensions.Options;

namespace CashFlow.Modules.Transactions.Infrastructure.Time;

internal sealed class BusinessCalendarOptions
{
    public const string SectionName = "BusinessCalendar";

    public string TimeZone { get; init; } = "America/Sao_Paulo";
}

/// <summary>
/// Needs the IANA database in the image: the API's Alpine image installs tzdata,
/// otherwise the time zone lookup fails at the first request.
/// </summary>
internal sealed class BusinessCalendar(IOptions<BusinessCalendarOptions> options) : IBusinessCalendar
{
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public DateOnly BusinessDateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _timeZone).DateTime);
}
