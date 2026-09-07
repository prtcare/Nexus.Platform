namespace Nexus.Governance.Contracts;

/// <summary>
/// Outcome of <see cref="IProductRegistry.RegisterProductAsync"/>. On success
/// <see cref="Product"/> is set and <see cref="FailureCode"/> is null; on failure the
/// product is null and the code is one of <see cref="DuplicateSlug"/> /
/// <see cref="InvalidName"/>.
/// </summary>
public sealed record RegisterProductResult(bool Succeeded, Product? Product, string? FailureCode)
{
    public const string DuplicateSlug = "DUPLICATE_SLUG";

    public const string InvalidName = "INVALID_NAME";
}
