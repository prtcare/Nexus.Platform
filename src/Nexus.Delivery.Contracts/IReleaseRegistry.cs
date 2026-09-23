using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>Why a release registration or lifecycle change was refused.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseRegistryRefusalReason
{
    None = 0,

    /// <summary>
    /// <b>The immutability rule.</b> The release id exists and the record presented is not byte-identical to
    /// the one held. Changed artifacts, changed hashes or changed release metadata always mean a NEW release,
    /// never an overwrite. Deliberately distinct from <see cref="ReleaseNotFound"/> and from a storage
    /// failure: this is the one refusal that is a governance decision rather than an error.
    /// </summary>
    ReleaseIdExistsWithDifferentContent,

    /// <summary>No such release.</summary>
    ReleaseNotFound,

    /// <summary>The resolved path escapes the registry root.</summary>
    PathEscapesRegistryRoot,

    /// <summary>The write failed at the storage layer.</summary>
    StorageFailure,

    /// <summary>The requested lifecycle change is not reachable from the state the release occupies.</summary>
    LifecycleTransitionIllegal,

    /// <summary>The release was withdrawn by an Owner act. Terminal; no transition leaves it.</summary>
    ReleaseIsWithdrawn
}

/// <summary>
/// A release's entry in the registry: identity, the digest of the record held, and lifecycle.
///
/// <para>
/// The record itself is not copied here, exactly as <see cref="ArtifactStoreEntry"/> does not hold bytes.
/// <see cref="RecordDigest"/> is what the immutability rule compares, and it is computed over
/// <see cref="ReleaseRecord.CanonicalForm"/> so that two records describing the same release produce the
/// same digest regardless of when each was written.
/// </para>
/// </summary>
public sealed record ReleaseRegistryEntry(
    ReleaseId ReleaseId,
    ArtifactDigest RecordDigest,
    ReleaseLifecycleState Lifecycle,
    DateTimeOffset RegisteredAt,
    DeploymentUnitId UnitId,
    string Version,
    BuildId BuildId,
    BundleId BundleId,
    string ReleaseRefName,
    string? LifecycleReason = null,
    ReleaseId? SupersededBy = null,
    string? OwnerRef = null);

/// <summary>The outcome of a registration. A refusal is an ordinary result, not an exception.</summary>
public sealed record ReleaseRegistrationOutcome
{
    private ReleaseRegistrationOutcome(
        ReleaseRegistryRefusalReason refusalReason,
        ReleaseRegistryEntry? entry,
        bool isAlreadyPresent,
        string? detail)
    {
        RefusalReason = refusalReason;
        Entry = entry;
        IsAlreadyPresent = isAlreadyPresent;
        Detail = detail;
    }

    public ReleaseRegistryRefusalReason RefusalReason { get; }

    public ReleaseRegistryEntry? Entry { get; }

    /// <summary>
    /// True when the release id already held a byte-identical record. Accepted as a no-op, not refused.
    ///
    /// <para>
    /// The reasoning matches <see cref="ArtifactPublishOutcome.IsAlreadyPresent"/>, and the distinction is
    /// worth restating because it is the difference between a retried pipeline succeeding and failing.
    /// Re-assembling identical inputs changes nothing, so refusing would make a retry fail while adding no
    /// protection. What must never be accepted is the same id with different content, and that is refused.
    /// </para>
    /// </summary>
    public bool IsAlreadyPresent { get; }

    /// <summary>Operator-facing. Must never contain a secret value.</summary>
    public string? Detail { get; }

    public bool IsAccepted => RefusalReason == ReleaseRegistryRefusalReason.None;

    public static ReleaseRegistrationOutcome Accepted(ReleaseRegistryEntry entry, bool alreadyPresent)
        => new(
            ReleaseRegistryRefusalReason.None,
            entry,
            alreadyPresent,
            alreadyPresent ? "Already registered with identical content." : null);

    public static ReleaseRegistrationOutcome Refused(ReleaseRegistryRefusalReason reason, string detail)
        => new(reason, null, false, detail);
}

/// <summary>What verifying a stored release established.</summary>
public sealed record ReleaseVerification(
    bool IsIntact,
    ReleaseId ReleaseId,
    ArtifactDigest? RecordedDigest,
    ArtifactDigest? RecomputedDigest,
    ReleaseLifecycleState? Lifecycle,
    string Detail)
{
    /// <summary>
    /// True only when the recomputed digest matches the recorded one. A mismatch means the registry's own
    /// contents changed, which is a corruption finding rather than a caller error, and it is reported
    /// separately from absence for that reason.
    /// </summary>
    public bool RegistryContentIntact => RecordedDigest is not null && RecomputedDigest is not null && RecordedDigest == RecomputedDigest;
}

/// <summary>
/// Release-level storage: the immutable Release Bundle and its lifecycle.
///
/// <para>
/// <b>Relationships to the two registries that already exist.</b> <see cref="IArtifactStore"/> holds build
/// output, addressed by <see cref="ArtifactId"/>. <see cref="IArtifactRegistry"/> holds the W9.1 bundle,
/// addressed by <see cref="BundleId"/>. This holds the <b>release</b>, addressed by
/// <see cref="ReleaseId"/> — the thing that is promoted through environments. Three granularities, three
/// authorities, and a release references the other two rather than restating either.
/// </para>
///
/// <para>
/// <b>Immutability is the registry's obligation.</b> Registering an existing <see cref="ReleaseId"/> whose
/// record digest differs must be refused by the registry itself, not by a caller remembering to check
/// first. That is the same obligation <see cref="IArtifactStore"/> carries for artifact bytes, applied one
/// level up: here the immutable thing is the release's metadata rather than its content.
/// </para>
///
/// <para>
/// <b>Provider-neutral by construction.</b> No member names a vendor. The first adapter is a local,
/// file-backed registry under a caller-supplied root; object-storage and OCI adapters are future
/// implementations that must not require a change to this interface.
/// </para>
/// </summary>
public interface IReleaseRegistry
{
    /// <summary>
    /// Registers a release. Refuses an existing id with different content; accepts an existing id with
    /// byte-identical content as a no-op.
    /// </summary>
    Task<ReleaseRegistrationOutcome> RegisterAsync(ReleaseRecord release, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(ReleaseId releaseId, CancellationToken cancellationToken = default);

    /// <summary>Reads the release bundle back. Null when absent; null for a malformed record would be indistinguishable, so a corrupt record is reported by <see cref="VerifyAsync"/> instead.</summary>
    Task<ReleaseRecord?> TryOpenAsync(ReleaseId releaseId, CancellationToken cancellationToken = default);

    Task<ReleaseRegistryEntry?> TryGetEntryAsync(ReleaseId releaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes the stored record's digest and compares it to the registered one. Separates "absent" from
    /// "the registry's own contents changed", which demand opposite responses.
    /// </summary>
    Task<ReleaseVerification> VerifyAsync(ReleaseId releaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a release's lifecycle state. Requires a reason: an unexplained state change cannot be reviewed,
    /// and "withdrawn" and "vanished" would otherwise look alike.
    /// </summary>
    Task<ReleaseRegistryEntry> SetLifecycleAsync(
        ReleaseId releaseId,
        ReleaseLifecycleState state,
        string reason,
        ReleaseId? supersededBy = null,
        CancellationToken cancellationToken = default);

    /// <summary>Every registered release, ordered by release id — deterministic across adapters.</summary>
    Task<IReadOnlyList<ReleaseRegistryEntry>> ListAsync(CancellationToken cancellationToken = default);
}
