namespace Nexus.ProductCore.Scope.Common;

public abstract class Entity<TId>
{
    protected Entity(TId id)
    {
        Id = id;
    }

    public TId Id { get; }
}
