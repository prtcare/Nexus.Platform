namespace Nexus.ProductCore.Scope.Common;

public abstract class AggregateRoot<TId> : Entity<TId>
{
    protected AggregateRoot(TId id)
        : base(id)
    {
    }
}
