namespace Nexus.ProductCore.Scope.Common.Identifiers;

public readonly record struct SubprojectId(Guid Value)
{
    public static SubprojectId New()
        => new(Guid.NewGuid());

    public override string ToString()
        => Value.ToString();
}
