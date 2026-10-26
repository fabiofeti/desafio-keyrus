namespace CashFlow.Api;

/// <summary>
/// Brings every module's schema up to date, one line per module that owns a
/// database. Runs only in the --migrate bootstrap mode, with administrative
/// credentials; the schemas hold no foreign key into each other, so the order
/// does not matter.
/// </summary>
internal static class DatabaseMigrations
{
    public static async Task ApplyAsync(IServiceProvider services)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        ILogger logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseMigrations));

        // Module migrations are added here by the add-module skill.

        logger.LogInformation("Database migrations applied");
    }
}
