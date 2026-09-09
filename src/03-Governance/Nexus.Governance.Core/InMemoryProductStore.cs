using Nexus.Governance.Contracts;

namespace Nexus.Governance.Core;

/// <summary>
/// Thread-safe in-memory <see cref="IProductStore"/>. A single lock guards every operation
/// because the add-if-slug-free check and the insert must be atomic; writes and reads are
/// serialised, which is fine for a dev/test store (replaced by an EF-backed store in
/// M-03-1.1's persistence work item).
/// </summary>
internal sealed class InMemoryProductStore : IProductStore
{
    private readonly object _gate = new();
    private readonly List<Product> _products = [];

    public Task<bool> TryAddAsync(Product product, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_products.Any(p => p.TenantId == product.TenantId && p.Slug == product.Slug))
            {
                return Task.FromResult(false);
            }

            _products.Add(product);
            return Task.FromResult(true);
        }
    }

    public Task<Product?> GetByIdAsync(string tenantId, ProductId productId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var match = _products.FirstOrDefault(p => p.TenantId == tenantId && p.Id == productId);
            return Task.FromResult(match);
        }
    }

    public Task<IReadOnlyList<Product>> ListByTenantAsync(string tenantId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<Product> snapshot = _products.Where(p => p.TenantId == tenantId).ToList();
            return Task.FromResult(snapshot);
        }
    }
}
