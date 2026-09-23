namespace Nexus.Delivery.Contracts;

/// <summary>
/// The artifact registry abstraction: content-addressed, immutable, and <b>cloud-portable</b>.
///
/// <para>
/// <b>What this interface is, and what it deliberately is not.</b> It is a neutral contract. Every
/// member is expressed in terms of <see cref="BundleId"/>, <see cref="ArtifactDigest"/> and
/// <see cref="ReleaseBundle"/> — no container registry, no object store, no cloud vendor appears in
/// any signature. That is the Owner's ruling: the abstraction stays portable and the backends remain
/// replaceable adapters. W9.1's first adapter is a deterministic local, file-backed store
/// (<c>Nexus.Delivery.Core.FileArtifactRegistry</c>); OCI, GitHub Packages and object-storage
/// adapters are future implementations of this same interface, and none of them may require a change
/// here.
/// </para>
///
/// <para>
/// <b>Immutability is the store's obligation, not the caller's.</b> W9.0 §4 recorded the requirement
/// in these words: <i>"immutability must be enforced by the store, not by convention — a store that
/// permits an overwrite and relies on people not doing it is the same class of control as the
/// protected-main situation W8-DEBT-01 records."</i> So <see cref="RegisterAsync"/> must refuse to
/// replace an existing entry, and a caller must be able to rely on that refusal rather than check
/// first and hope nothing raced it.
/// </para>
///
/// <para>
/// <b>Retention is part of the contract, not an operational afterthought.</b> <see cref="TryOpenAsync"/>
/// exists because <c>ROLLBACK_MODEL.md</c> depends on the previous bundle still being retrievable: an
/// unrehearsed rollback whose bundle has been pruned is a plan, not a capability.
/// </para>
/// </summary>
public interface IArtifactRegistry
{
    /// <summary>
    /// Registers a bundle. Write-once: an existing <see cref="BundleId"/> is refused with
    /// <see cref="RegistryRefusalReason.BundleIdAlreadyExists"/> rather than overwritten.
    /// </summary>
    Task<RegistrationOutcome> RegisterAsync(ReleaseBundle bundle, CancellationToken cancellationToken = default);

    /// <summary>Whether a bundle id is present. Used to gate a rollback's availability before offering it.</summary>
    Task<bool> ExistsAsync(BundleId bundleId, CancellationToken cancellationToken = default);

    /// <summary>Reads a stored bundle's manifest, or null when it is not present.</summary>
    Task<ReleaseBundle?> TryOpenAsync(BundleId bundleId, CancellationToken cancellationToken = default);

    /// <summary>Reads a bundle's entry (identity and digests) without materialising the manifest.</summary>
    Task<ArtifactRegistryEntry?> TryGetEntryAsync(BundleId bundleId, CancellationToken cancellationToken = default);

    /// <summary>All registered entries, ordered by registration time then bundle id — deterministic across adapters.</summary>
    Task<IReadOnlyList<ArtifactRegistryEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a bundle quarantined. The registry records the state; it does not decide, because the
    /// decision is Owner-reserved (<c>DEPLOYMENT_GOVERNANCE_MODEL.md</c> §5).
    /// </summary>
    Task QuarantineAsync(BundleId bundleId, string reason, CancellationToken cancellationToken = default);
}
