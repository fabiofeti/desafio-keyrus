using System.Reflection;
using CashFlow.Modules.Consolidation.Application.Abstractions.Data;
using CashFlow.Modules.Consolidation.Presentation;
using CashFlow.Modules.Transactions.Application.Abstractions.Data;
using CashFlow.Modules.Transactions.Presentation;

namespace CashFlow.Hosting;

/// <summary>
/// The module list, in one place, used by the API and by the Worker - the
/// modules are composed by both hosts, never duplicated per host.
/// </summary>
/// <remarks>
/// An architecture test compares this list with the folders under
/// <c>src/Modules</c>, so a module cannot be added and quietly left unregistered.
/// </remarks>
public static class Modules
{
    /// <summary>Assemblies scanned for command/query handlers and validators.</summary>
    public static readonly Assembly[] ApplicationAssemblies =
    [
        typeof(ITransactionsDbContext).Assembly,
        typeof(IConsolidationDbContext).Assembly
    ];

    /// <summary>Assemblies scanned for <c>IEndpoint</c> (API) and integration event handlers (Worker).</summary>
    public static readonly Assembly[] PresentationAssemblies =
    [
        typeof(TransactionsPresentation).Assembly,
        typeof(ConsolidationPresentation).Assembly
    ];
}
