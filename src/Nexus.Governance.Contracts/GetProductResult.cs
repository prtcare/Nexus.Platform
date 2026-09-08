namespace Nexus.Governance.Contracts;

/// <summary>
/// Outcome of <see cref="IProductRegistry.GetProductAsync"/>. <see cref="Found"/> is false
/// when no product with the requested id exists in the requested tenant.
/// </summary>
public sealed record GetProductResult(bool Found, Product? Product);
