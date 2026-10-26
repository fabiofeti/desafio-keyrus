namespace CashFlow.Common.Domain;

/// <summary>
/// Base for aggregate roots. Deliberately empty: there is no in-process domain
/// event dispatcher in this solution. The only fact that leaves a module is an
/// integration event, written explicitly to that module's outbox in the same
/// transaction as the state change it describes.
/// </summary>
public abstract class Entity
{
    protected Entity()
    {
    }
}
