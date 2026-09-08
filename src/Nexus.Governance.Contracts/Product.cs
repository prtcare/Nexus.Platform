namespace Nexus.Governance.Contracts;

/// <summary>
/// The L03 GOVERNANCE product record: the authoritative statement that a product exists and
/// is ours. Nexus Forge references a <see cref="ProductId"/>; it never defines its own
/// product identity (WORK_UNIVERSE.md §3). TenantId is a plain string today; real tenant
/// enforcement is M-01-2.1 (deferred).
/// </summary>
public sealed record Product(
    ProductId Id,
    string TenantId,
    string Name,
    ProductSlug Slug,
    ProductClassification Classification,
    ProductLifecycleState LifecycleState);
