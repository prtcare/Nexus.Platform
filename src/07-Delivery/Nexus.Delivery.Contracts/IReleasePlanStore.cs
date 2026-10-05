using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// Why the governed Release Plan store refused a write.
///
/// <para>
/// <b>Refusals, not exceptions</b>, mirroring <see cref="ReleaseRegistryRefusalReason"/>. A caller that
/// cannot tell "the plan is invalid" from "someone else wrote first" from "the disk failed" cannot act on
/// any of them, and the estate's standing finding is that a failure collapsed into a generic error is a
/// failure nobody can fix.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleasePlanWriteRefusalReason
{
    None = 0,

    /// <summary>The record violates a well-formedness or coherence rule. The verdict names which.</summary>
    PlanInvalid,

    /// <summary>A plan already exists for this release and the content presented is not identical to it.</summary>
    ReleaseIdAlreadyHasAPlan,

    /// <summary>No plan exists for this release, so there is nothing to update.</summary>
    PlanNotFound,

    /// <summary>
    /// The stored version is not the one the caller expected. Another writer got there first, and the update
    /// was built against a state that no longer holds.
    /// </summary>
    ConcurrentModification,

    /// <summary>The version this write would produce is already on disk. A version number is never reused.</summary>
    VersionAlreadyRecorded,

    /// <summary>The release id does not resolve inside the store root.</summary>
    PathEscapesStoreRoot,

    /// <summary>The write could not be completed. The detail names the failure; no state was changed.</summary>
    StorageFailure
}

/// <summary>The result of one governed write, accepted or refused, with the hashes on both sides of it.</summary>
/// <param name="IsAccepted">True only when the new version is durable.</param>
/// <param name="RefusalReason">The typed reason, or <see cref="ReleasePlanWriteRefusalReason.None"/>.</param>
/// <param name="Version">The version written, or the version that is still current when refused.</param>
/// <param name="BeforeHash">
/// The canonical plan digest before the write, or <see langword="null"/> when there was no plan. Recorded on
/// refusals too, so a refused write is as auditable as an accepted one.
/// </param>
/// <param name="AfterHash">The canonical plan digest after the write. Equal to <paramref name="BeforeHash"/> when nothing changed.</param>
/// <param name="Detail">Operator-facing. <b>Never a credential value</b> — this text reaches build and deployment logs.</param>
public sealed record ReleasePlanWriteOutcome(
    bool IsAccepted,
    ReleasePlanWriteRefusalReason RefusalReason,
    int Version,
    ArtifactDigest? BeforeHash,
    ArtifactDigest? AfterHash,
    string Detail)
{
    /// <summary>True when the store already held a byte-identical record at the same version. A no-op, not a second write.</summary>
    public bool IsAlreadyPresent { get; init; }

    /// <summary>The plan as it now stands, whatever the outcome.</summary>
    public ReleaseSecurityPlan? Plan { get; init; }

    /// <summary>The validation verdict for the record offered, when validation ran.</summary>
    public ReleaseSecurityPlanVerdict? Verdict { get; init; }

    public static ReleasePlanWriteOutcome Accepted(
        int version,
        ArtifactDigest? beforeHash,
        ArtifactDigest afterHash,
        ReleaseSecurityPlan plan,
        string detail,
        bool alreadyPresent = false)
        => new(true, ReleasePlanWriteRefusalReason.None, version, beforeHash, afterHash, detail)
        {
            Plan = plan,
            IsAlreadyPresent = alreadyPresent
        };

    public static ReleasePlanWriteOutcome Refused(
        ReleasePlanWriteRefusalReason reason,
        int version,
        ArtifactDigest? beforeHash,
        ArtifactDigest? afterHash,
        string detail,
        ReleaseSecurityPlanVerdict? verdict = null,
        ReleaseSecurityPlan? plan = null)
        => new(false, reason, version, beforeHash, afterHash, detail)
        {
            Verdict = verdict,
            Plan = plan
        };
}

/// <summary>
/// One accepted write, as recorded in the store's append-only application ledger.
///
/// <para>
/// <b>Before and after, together.</b> A ledger that recorded only the resulting state could not answer "what
/// changed on this write", which is the question asked of it when a release state moves for a reason nobody
/// expected. Both hashes are of the <i>canonical projection</i>, not of the stored bytes, so the pair stays
/// meaningful if the storage encoding ever changes.
/// </para>
/// </summary>
public sealed record ReleasePlanApplicationRecord(
    ReleaseId ReleaseId,
    int Version,
    int PreviousVersion,
    DateTimeOffset AppliedAt,
    string AppliedBy,
    string Reason,
    string Mode,
    ArtifactDigest? BeforeHash,
    ArtifactDigest AfterHash,
    string? HumanDecisionReference,
    string? CredentialRotationDecisionReference)
{
    /// <summary>The stored-bytes hash of the record this version wrote, so a tampered file is distinguishable from a rewritten plan.</summary>
    public ArtifactDigest? StoredBytesHash { get; init; }
}

/// <summary>
/// The typed update a caller offers the governed writer.
///
/// <para>
/// <b>Nullable members mean "leave unchanged".</b> An update is a request to move one or more members, not a
/// replacement record; a caller that had to restate every member would eventually restate one wrongly, and
/// the member it restated wrongly would be the one it was not thinking about.
/// </para>
///
/// <para>
/// <b>Optimistic concurrency, not a lock.</b> <see cref="ExpectedVersion"/> is required and is compared to
/// what is stored. A caller that read version 1 and offers an update built on it cannot silently overwrite a
/// version 2 written in between — which is the whole reason a hand-edited file was unacceptable.
/// </para>
/// </summary>
public sealed record ReleaseSecurityPlanUpdate
{
    public required ReleaseId ReleaseId { get; init; }

    /// <summary>The version the caller believes is current. A mismatch is refused, never merged.</summary>
    public required int ExpectedVersion { get; init; }

    public CredentialRotationStatus? CredentialRotationStatus { get; init; }

    public string? CredentialRotationDecisionReference { get; init; }

    public ReleaseReferenceProtectionMode? ReleaseReferenceProtectionMode { get; init; }

    public bool? ReleaseRefServerSideProtectionVerified { get; init; }

    public bool? CompensatingControlApproved { get; init; }

    public bool? CompensatingControlVerified { get; init; }

    public ReleaseReferenceProtectionScope? AllowedEnvironmentScope { get; init; }

    public string? HumanDecisionReference { get; init; }

    public DeploymentAuthorityRole? HumanDecisionAuthority { get; init; }

    /// <summary>Required. An unattributed change is one nobody can be asked about.</summary>
    public required string UpdatedBy { get; init; }

    /// <summary>Required. The reason travels with the version it produced.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Supplied by the caller rather than read from a clock inside the store: a clock read during a write makes
    /// the resulting digest unreproducible, and a plan digest that cannot be reproduced cannot be compared.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Applies this update to the record it was built against, producing the next version.</summary>
    public ReleaseSecurityPlan ApplyTo(ReleaseSecurityPlan current)
    {
        ArgumentNullException.ThrowIfNull(current);

        return current with
        {
            CredentialRotationStatus = CredentialRotationStatus ?? current.CredentialRotationStatus,
            CredentialRotationDecisionReference = CredentialRotationDecisionReference ?? current.CredentialRotationDecisionReference,
            ReleaseReferenceProtectionMode = ReleaseReferenceProtectionMode ?? current.ReleaseReferenceProtectionMode,
            ReleaseRefServerSideProtectionVerified = ReleaseRefServerSideProtectionVerified ?? current.ReleaseRefServerSideProtectionVerified,
            CompensatingControlApproved = CompensatingControlApproved ?? current.CompensatingControlApproved,
            CompensatingControlVerified = CompensatingControlVerified ?? current.CompensatingControlVerified,
            AllowedEnvironmentScope = AllowedEnvironmentScope ?? current.AllowedEnvironmentScope,
            HumanDecisionReference = HumanDecisionReference ?? current.HumanDecisionReference,
            HumanDecisionAuthority = HumanDecisionAuthority ?? current.HumanDecisionAuthority,
            UpdatedAt = UpdatedAt ?? current.UpdatedAt,
            UpdatedBy = UpdatedBy,
            Reason = Reason,
            Version = current.Version + 1
        };
    }
}

/// <summary>Whether a governed plan could be read, and if not, why not.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleasePlanReadState
{
    /// <summary>A plan exists and decoded.</summary>
    Present = 0,

    /// <summary>No plan has been recorded for this release.</summary>
    Absent,

    /// <summary>
    /// A file is there but does not decode. Distinct from <see cref="Absent"/> on purpose: a corrupt authority
    /// must never read as "nothing recorded", because the two are remedied by different people.
    /// </summary>
    Corrupt
}

/// <summary>The outcome of reading the governed plan for one release.</summary>
public sealed record ReleasePlanReadResult(ReleasePlanReadState State, ReleaseId ReleaseId, ReleaseSecurityPlan? Plan, string Detail)
{
    public bool IsPresent => State == ReleasePlanReadState.Present;
}

/// <summary>
/// The governed writer for <see cref="ReleaseSecurityPlan"/>.
///
/// <para>
/// <b>An interface, and the file adapter is the first implementation.</b> Same reasoning as
/// <see cref="IReleaseRegistry"/> one level down: nothing about a filesystem may bias the contract, and if it
/// does, that is discovered here for the cost of a test rather than when a database-backed or
/// service-backed authority is written. This is deliberately <b>not</b> a general configuration store — it
/// has one record type, one key, and one job.
/// </para>
/// </summary>
public interface IReleasePlanStore
{
    /// <summary>The current authoritative plan, or a typed statement that there is none.</summary>
    Task<ReleasePlanReadResult> ReadAsync(ReleaseId releaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the first version of a release's plan. Refused if a <i>different</i> record is already held;
    /// an identical re-presentation is a no-op, so a retried run succeeds rather than failing for having
    /// already succeeded.
    /// </summary>
    Task<ReleasePlanWriteOutcome> CreateAsync(ReleaseSecurityPlan plan, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a typed update under optimistic concurrency, validating the result before it is written and
    /// refusing any unknown or contradictory state.
    /// </summary>
    Task<ReleasePlanWriteOutcome> ApplyAsync(ReleaseSecurityPlanUpdate update, CancellationToken cancellationToken = default);

    /// <summary>The append-only ledger of accepted writes, with the before and after hashes of each.</summary>
    Task<IReadOnlyList<ReleasePlanApplicationRecord>> ReadApplicationsAsync(ReleaseId releaseId, CancellationToken cancellationToken = default);
}
