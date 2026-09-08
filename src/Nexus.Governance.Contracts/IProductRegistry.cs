namespace Nexus.Governance.Contracts;

/// <summary>
/// L03 GOVERNANCE product-registry port (the M-03-1.1 command/query shape that replaced the
/// old seed's <c>IsProductRegisteredAsync</c> / string-returning <c>ListProductsAsync</c>).
/// Registration always creates a <see cref="ProductLifecycleState.Proposed"/> product;
/// lifecycle transitions are M-03-1.3.
/// </summary>
public interface IProductRegistry
{
    Task<RegisterProductResult> RegisterProductAsync(
        string tenantId,
        string name,
        string slug,
        ProductClassification classification,
        CancellationToken ct = default);

    Task<GetProductResult> GetProductAsync(
        string tenantId,
        ProductId productId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Product>> ListProductsAsync(
        string tenantId,
        CancellationToken ct = default);
}
