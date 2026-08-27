namespace Nexus.ProductCore.Scope.Common.Identifiers;

public readonly record struct ProjectId(Guid Value)
{
    public static ProjectId New()
        => new(Guid.NewGuid());

    public override string ToString()
        => Value.ToString();
}
