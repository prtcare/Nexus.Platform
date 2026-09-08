namespace Nexus.Governance.Contracts;

/// <summary>
/// Typed identifier of a <see cref="Product"/> (L03 GOVERNANCE). A plain wrapped string;
/// equality is by <see cref="Value"/>.
/// </summary>
public sealed record ProductId
{
    public ProductId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("ProductId value must not be null or whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
