using System.Reflection;

namespace CashFlow.ArchitectureTests;

/// <summary>
/// The assemblies under test, and the repository root the module tests read
/// from disk. Adding a module means adding it here as well as to
/// <c>Hosting.Modules</c>; <see cref="ModuleTests"/> fails if either is forgotten.
/// </summary>
public static class Assemblies
{
    public static readonly Assembly CommonDomain =
        typeof(Common.Domain.Entity).Assembly;

    public static readonly Assembly CommonApplication =
        typeof(Common.Application.ApplicationConfiguration).Assembly;

    public static readonly Assembly CommonInfrastructure =
        typeof(Common.Infrastructure.InfrastructureConfiguration).Assembly;

    public static readonly Assembly CommonPresentation =
        typeof(Common.Presentation.Endpoints.IEndpoint).Assembly;

    public static readonly Assembly Api = typeof(Program).Assembly;

    public static readonly Assembly Worker = typeof(Worker.WorkerComposition).Assembly;

    /// <summary>Module assemblies. Entries are added by the add-module skill.</summary>
    public static IReadOnlyList<ModuleAssemblies> Modules =>
    [
        new(
            "Transactions",
            typeof(CashFlow.Modules.Transactions.Domain.TransactionsDomain).Assembly,
            typeof(CashFlow.Modules.Transactions.Application.Abstractions.Data.ITransactionsDbContext).Assembly,
            typeof(CashFlow.Modules.Transactions.Infrastructure.TransactionsModule).Assembly,
            typeof(CashFlow.Modules.Transactions.Presentation.TransactionsPresentation).Assembly,
            typeof(CashFlow.Modules.Transactions.IntegrationEvents.TransactionsIntegrationEvents).Assembly)
    ];

    public static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CashFlow.slnx")))
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new InvalidOperationException("The repository root could not be located from the test output directory.");
    }
}

public sealed record ModuleAssemblies(
    string Name,
    Assembly Domain,
    Assembly Application,
    Assembly Infrastructure,
    Assembly Presentation,
    Assembly IntegrationEvents);
