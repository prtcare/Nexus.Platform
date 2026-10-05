using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>Where an artifact is in its lifecycle. Recorded, not inferred.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArtifactLifecycleState
{
    /// <summary>Published and fetchable.</summary>
    Published,

    /// <summary>Must not be promoted or fetched for deployment. Set by an Owner decision or a certification failure discovered after publication.</summary>
    Quarantined,

    /// <summary>Superseded by a later artifact for the same unit. Retained for rollback; not promoted.</summary>
    Retired
}

/// <summary>Why a publish was refused.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArtifactPublishRefusalReason
{
    None = 0,

    /// <summary>The id exists with different bytes. This is the immutability rule: changed bytes are a NEW build.</summary>
    ArtifactIdExistsWithDifferentBytes,

    /// <summary>
    /// The id was withdrawn by a governed act and may never be written to again. <b>Distinct from
    /// <see cref="ArtifactIdExistsWithDifferentBytes"/></b> because the two route to different actions: a
    /// binding says "use the same bytes or a new version", and a withdrawal says "this version is spent,
    /// there is no payload to match". Collapsing them would send a reader looking for bytes that no longer
    /// exist and are not supposed to.
    /// </summary>
    ArtifactIdWithdrawn,

    /// <summary>The bytes presented do not hash to the digest the artifact claims.</summary>
    ContentHashMismatch,

    /// <summary>The source file to publish does not exist.</summary>
    SourceFileMissing,

    /// <summary>The resolved path escapes the store root.</summary>
    PathEscapesStoreRoot,

    /// <summary>The write failed at the storage layer.</summary>
    StorageFailure
}

/// <summary>An artifact's entry in the store: identity, provenance and lifecycle. Never its bytes.</summary>
public sealed record ArtifactStoreEntry(
    ArtifactId ArtifactId,
    BuildId BuildId,
    ArtifactDigest ContentDigest,
    long SizeBytes,
    DateTimeOffset PublishedAt,
    ArtifactLifecycleState Lifecycle = ArtifactLifecycleState.Published,
    string? LifecycleReason = null,
    ArtifactId? SupersededBy = null,
    string? FileName = null);

/// <summary>The outcome of a publish. A refusal is an ordinary result, not an exception.</summary>
public sealed record ArtifactPublishOutcome
{
    private ArtifactPublishOutcome(
        ArtifactPublishRefusalReason refusalReason,
        ArtifactStoreEntry? entry,
        bool isAlreadyPresent,
        string? detail)
    {
        RefusalReason = refusalReason;
        Entry = entry;
        IsAlreadyPresent = isAlreadyPresent;
        Detail = detail;
    }

    public ArtifactPublishRefusalReason RefusalReason { get; }

    public ArtifactStoreEntry? Entry { get; }

    /// <summary>
    /// True when the id already held byte-identical content. Accepted as a no-op, not refused.
    ///
    /// <para>
    /// The distinction is worth stating because it differs from W9.1's bundle registry, which refuses any
    /// second write. There, the rule is "a changed artifact is a NEW bundle, never an overwrite" — and an
    /// idempotent re-publish of *identical* bytes changes nothing, so refusing it would make a retried
    /// pipeline run fail for no reason while adding no protection. What must never be accepted is the
    /// same id with different bytes, and that is refused.
    /// </para>
    /// </summary>
    public bool IsAlreadyPresent { get; }

    /// <summary>Operator-facing detail. Must never contain a secret value.</summary>
    public string? Detail { get; }

    public bool IsAccepted => RefusalReason == ArtifactPublishRefusalReason.None;

    public static ArtifactPublishOutcome Accepted(ArtifactStoreEntry entry, bool alreadyPresent)
        => new(ArtifactPublishRefusalReason.None, entry, alreadyPresent, alreadyPresent ? "Already present with identical bytes." : null);

    public static ArtifactPublishOutcome Refused(ArtifactPublishRefusalReason reason, string detail)
        => new(reason, null, false, detail);
}

/// <summary>The outcome of verifying stored bytes against an expected digest.</summary>
public sealed record ArtifactHashVerification(
    bool IsMatch,
    ArtifactDigest Expected,
    ArtifactDigest? Recorded,
    ArtifactDigest? Recomputed,
    string Detail)
{
    /// <summary>
    /// True only when the recomputed digest matches the expectation AND the recorded digest. A mismatch
    /// between recorded and recomputed means the store's own bytes changed, which is a corruption finding
    /// rather than a caller error, and it is reported separately for that reason.
    /// </summary>
    public bool StoreContentIntact => Recorded is not null && Recomputed is not null && Recorded == Recomputed;
}

/// <summary>What a fetch produced.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArtifactFetchStatus
{
    /// <summary>The bytes were written to the destination and matched the recorded digest.</summary>
    Fetched,

    /// <summary>No such artifact.</summary>
    Absent,

    /// <summary>
    /// The artifact is present but its bytes do not match the recorded digest.
    ///
    /// <para>
    /// A distinct outcome rather than a flavour of absence, because the two demand opposite responses:
    /// an absent artifact is a missing promotion input, while a corrupt one means the store's own
    /// contents have changed and every artifact from that store is now suspect. Returning <c>false</c>
    /// for both — which is what a boolean fetch would do — makes a corruption incident look like a
    /// typo in an id.
    /// </para>
    /// </summary>
    Corrupt,

    /// <summary>The artifact exists but is quarantined; it was deliberately not fetched.</summary>
    Quarantined
}

/// <summary>The result of a fetch.</summary>
public sealed record ArtifactFetchOutcome(ArtifactFetchStatus Status, string? Detail = null)
{
    public bool IsFetched => Status == ArtifactFetchStatus.Fetched;

    public static ArtifactFetchOutcome Fetched() => new(ArtifactFetchStatus.Fetched);

    public static ArtifactFetchOutcome Absent() => new(ArtifactFetchStatus.Absent, "No such artifact.");

    public static ArtifactFetchOutcome Corrupt(string detail) => new(ArtifactFetchStatus.Corrupt, detail);

    public static ArtifactFetchOutcome Quarantined(string reason) => new(ArtifactFetchStatus.Quarantined, reason);
}

/// <summary>
/// Artifact-level storage: content-addressed in practice, coordinate-addressed in its interface.
///
/// <para>
/// <b>Relationships to W9.1's <see cref="IArtifactRegistry"/>.</b> The two are different granularities and
/// both are needed. A <c>ReleaseBundle</c> is a *release* — the set of units promoted together, recorded
/// after certification. An artifact is a *build output* — what one build of one unit produced, recorded
/// before anything is promoted. Bundles reference artifacts; artifacts do not know about bundles. W9.1
/// built the bundle-level registry and this is the artifact-level one, and a future Release Bundle is
/// assembled from entries in this store.
/// </para>
///
/// <para>
/// <b>Provider-neutral by construction.</b> No member names a vendor, and a W9.1 test already asserts that
/// property for the registry contract; the same rule applies here. The first adapter is a local,
/// file-backed store; OCI, package-feed and object-storage adapters are future implementations that must
/// not require a change to this interface.
/// </para>
///
/// <para>
/// <b>Immutability is the store's obligation.</b> Publishing an existing <see cref="ArtifactId"/> with
/// different bytes must be refused by the store itself, not by a caller remembering to check first.
/// </para>
/// </summary>
public interface IArtifactStore
{
    /// <summary>
    /// Publishes an artifact from a file on disk. Refuses an existing id with different bytes; accepts an
    /// existing id with identical bytes as a no-op.
    /// </summary>
    Task<ArtifactPublishOutcome> PublishAsync(PackagedArtifact artifact, string sourceFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches an artifact's bytes to <paramref name="destinationPath"/>, verifying them against the
    /// recorded digest on the way out. Absence, corruption and quarantine are distinct outcomes.
    /// </summary>
    Task<ArtifactFetchOutcome> FetchAsync(ArtifactId artifactId, string destinationPath, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(ArtifactId artifactId, CancellationToken cancellationToken = default);

    /// <summary>Recomputes the stored bytes' digest and compares it to <paramref name="expected"/> and to the recorded digest.</summary>
    Task<ArtifactHashVerification> VerifyHashAsync(ArtifactId artifactId, ArtifactDigest expected, CancellationToken cancellationToken = default);

    Task<ArtifactStoreEntry?> ResolveByArtifactIdAsync(ArtifactId artifactId, CancellationToken cancellationToken = default);

    /// <summary>Every artifact produced by one build, ordered by artifact id — deterministic across adapters.</summary>
    Task<IReadOnlyList<ArtifactStoreEntry>> ResolveByBuildIdAsync(BuildId buildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an artifact's lifecycle state. A quarantine or a retirement requires a reason: an unexplained
    /// state change cannot be reviewed, and the difference between "retired" and "vanished" is the whole
    /// reason the state is recorded rather than the file being deleted.
    /// </summary>
    Task<ArtifactStoreEntry> SetLifecycleAsync(
        ArtifactId artifactId,
        ArtifactLifecycleState state,
        string reason,
        ArtifactId? supersededBy = null,
        CancellationToken cancellationToken = default);
}
