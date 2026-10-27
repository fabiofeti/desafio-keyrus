namespace CashFlow.Common.Infrastructure.Health;

public static class HealthTags
{
    /// <summary>
    /// Checks a host must pass before it takes traffic (<c>/health/ready</c>).
    /// Liveness (<c>/health/live</c>) runs none: a database outage makes the API
    /// unready, not dead, and restarting it would not bring the database back.
    /// </summary>
    public const string Ready = "ready";
}
