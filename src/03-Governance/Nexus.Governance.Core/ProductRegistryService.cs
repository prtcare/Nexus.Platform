using Nexus.Governance.Contracts;

namespace Nexus.Governance.Core;

/// <summary>
/// L03 GOVERNANCE product-registry service. Enforces the product invariants:
/// a blank name is refused (INVALID_NAME); a duplicate slug within the SAME tenant is refused
/// (DUPLICATE_SLUG) and no row is stored; the same slug under a DIFFERENT tenant succeeds;
/// reads are tenant-scoped; a new product's lifecycle state is always Proposed.
/// </summary>
public sealed class ProductRegistryService : IProductRegistry
{
    private readonly IProductStore _store;

    internal ProductRegistryService(IProductStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<RegisterProductResult> RegisterProductAsync(
        string tenantId,
        string name,
        string slug,
        ProductClassification classification,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new RegisterProductResult(false, null, RegisterProductResult.InvalidName);
        }

        // ProductSlug's constructor enforces the slug format; an invalid slug surfaces as an
        // ArgumentException rather than ever becoming a stored row.
        var product = new Product(
            new ProductId(Guid.NewGuid().ToString("N")),
            tenantId,
            name,
            new ProductSlug(slug),
            classification,
            ProductLifecycleState.Proposed);

        var stored = await _store.TryAddAsync(product, ct).ConfigureAwait(false);

        return stored
            ? new RegisterProductResult(true, product, null)
            : new RegisterProductResult(false, null, RegisterProductResult.DuplicateSlug);
    }

    public async Task<GetProductResult> GetProductAsync(
        string tenantId,
        ProductId productId,
        CancellationToken ct = default)
    {
        var product = await _store.GetByIdAsync(tenantId, productId, ct).ConfigureAwait(false);

        return product is null
            ? new GetProductResult(false, null)
            : new GetProductResult(true, product);
    }

    public async Task<IReadOnlyList<Product>> ListProductsAsync(
        string tenantId,
        CancellationToken ct = default)
        => await _store.ListByTenantAsync(tenantId, ct).ConfigureAwait(false);
}
