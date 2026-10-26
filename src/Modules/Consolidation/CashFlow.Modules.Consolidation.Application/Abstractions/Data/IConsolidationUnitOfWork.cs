using CashFlow.Common.Application.Abstractions;

namespace CashFlow.Modules.Consolidation.Application.Abstractions.Data;

/// <summary>
/// This module's transaction, and nobody else's. The shared
/// <see cref="IUnitOfWork"/> is never registered against a concrete context:
/// the Worker hosts both modules, and a second registration would silently win.
/// </summary>
public interface IConsolidationUnitOfWork : IUnitOfWork;
