using Nexus.Governance.Contracts;

namespace Nexus.Governance.Core;

/// <summary>
/// Persistence seam for the product registry. The in-memory implementation in this assembly
/// backs the registry until M-03-1.1's persistence work item (WI-03-1.1.2) adds
/// Nexus.Governance.Infrastructure with an EF-backed store over the governance schema.
///
/// When that Infrastructure project lands, this interface is promoted to public (or exposed
/// via InternalsVisibleTo) so the Infrastructure assembly can implement the SAME abstraction.
/// The semantics below are exactly the EF mapping targets:
///   - <see cref="TryAddAsync"/> enforces the per-tenant unique index on (TenantId, Slug);
///   - <see cref="GetByIdAsync"/> filters by TenantId AND Id;
///   - <see cref="ListByTenantAsync"/> filters by TenantId.
/// </summary>
internal interface IProductStore
{
    /// <summary>
    /// Adds the product only when no product with the same (TenantId, Slug) already exists.
    /// Returns true when stored; false when a duplicate slug exists in that tenant.
    /// </summary>
    Task<bool> TryAddAsync(Product product, CancellationToken ct = default);

    /// <summary>Returns the tenant-scoped product by id, or null when not found.</summary>
    Task<Product?> GetByIdAsync(string tenantId, ProductId productId, CancellationToken ct = default);

    /// <summary>Returns only the products that belong to the given tenant.</summary>
    Task<IReadOnlyList<Product>> ListByTenantAsync(string tenantId, CancellationToken ct = default);
}
