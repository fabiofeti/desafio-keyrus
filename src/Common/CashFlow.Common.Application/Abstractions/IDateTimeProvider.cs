namespace CashFlow.Common.Application.Abstractions;

/// <summary>
/// Time as a dependency. Application code never calls DateTime.UtcNow directly,
/// so a test can decide what "now" is instead of racing it.
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
