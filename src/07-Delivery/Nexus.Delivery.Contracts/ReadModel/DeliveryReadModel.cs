using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts.ReadModel;

/// <summary>
/// W10.1 — <b>the published Delivery read model.</b> A projection of Delivery authority, for a
/// consumer that must never read Delivery's store.
///
/// <para>
/// <b>The projection is not an authority.</b> Delete it and Delivery is unchanged. If it disagrees
/// with Delivery, Delivery wins. Nothing may write back through it — it is produced by the owner,
/// from the owner's own store, and consumed read-only.
/// </para>
///
/// <para>
/// <b>Why a Delivery-specific contract rather than an existing one.</b> W10.1 TASK 1 measured the
/// consumer's existing contract (<c>atlas.control-read-model.v1</c>) and found its panel key set and
/// item schema are both closed: seven string fields survive, with no version, environment, digest,
/// commit or timestamp. Delivery's facts cannot be expressed in it, and smuggling them into
/// <c>name</c>/<c>status</c> strings would validate while carrying nothing. See
/// <c>ATLAS_EXISTING_READ_CONTRACT.md</c>.
/// </para>
///
/// <para>
/// <b>Authority is classified, never flattened.</b> Every fact carries a
/// <see cref="DeliveryAuthorityClass"/>. A certification recorded in 2026 is <i>historical
/// evidence</i>; it is not an observation of what is running now, and nothing here claims runtime
/// health. That distinction is the whole reason the class exists, and collapsing it is the failure
/// TASK 5 forbids.
/// </para>
/// </summary>
public sealed record DeliveryReadModel(
    string SchemaVersion,
    DeliveryReadSource Source,
    DeliveryReadPayload Payload);

/// <summary>
/// Who published this, from what, and when. <b>Not identity.</b>
///
/// <para>
/// The <see cref="PayloadDigest"/> is the semantic identity: it is computed over the payload alone,
/// so republishing unchanged authority state produces the same digest even though
/// <see cref="ObservedAt"/> and <see cref="PublishedAt"/> have moved. That is what lets a consumer
/// tell a metadata refresh from an actual Delivery-state change (TASK 9) — and it is why no
/// timestamp may enter the digest.
/// </para>
/// </summary>
public sealed record DeliveryReadSource(
    string ContractVersion,
    string Authority,
    string SourceId,
    string SourceRevision,
    string ObservedAt,
    string PublishedAt,
    string PayloadDigest);

/// <summary>
/// Everything the projection asserts, and nothing else.
///
/// <para>
/// Deliberately flat and additive: a consumer reads the collections it understands and ignores the
/// rest. No collection is a summary of another, so nothing has to be summed to be correct.
/// </para>
/// </summary>
public sealed record DeliveryReadPayload(
    IReadOnlyList<DeliveryReadBuild> Builds,
    IReadOnlyList<DeliveryReadArtifact> Artifacts,
    IReadOnlyList<DeliveryReadArtifactIdentity> ArtifactIdentities,
    IReadOnlyList<DeliveryReadRelease> Releases,
    IReadOnlyList<DeliveryReadDeployment> Deployments,
    IReadOnlyList<DeliveryReadEnvironment> Environments,
    IReadOnlyList<DeliveryReadVerification> Verifications,
    IReadOnlyList<DeliveryReadPromotion> Promotions,
    IReadOnlyList<DeliveryReadMigration> Migrations,
    DeliveryReadBackup Backup,
    IReadOnlyList<DeliveryReadRollback> Rollbacks,
    DeliveryReadReferenceProtection ReferenceProtection,
    DeliveryReadProdReadiness ProdReadiness,
    IReadOnlyList<DeliveryReadLineageEdge> LineageEdges,
    IReadOnlyList<DeliveryReadGap> Gaps);

/// <summary>
/// The production position, stated in the authority's own terms.
///
/// <para>
/// <b>The distinction this record exists to protect.</b> Production is <b>deferred by the Owner</b>,
/// not failed, and not started — and a projection that rendered any of those three as the others
/// would be lying about a decision a human made deliberately. <see cref="State"/> is
/// <c>DEFERRED_BY_OWNER</c> and <see cref="DeploymentState"/> is <c>NOT_STARTED</c>, sourced from
/// the release plan's recorded Owner decision and from the absence of any production deployment in
/// the ledger — never from an inference about why production is quiet.
/// </para>
/// </summary>
public sealed record DeliveryReadProdReadiness(
    string State,
    string DeploymentState,
    string DecisionReference,
    string DecisionAuthority,
    DeliveryAuthorityClass Authority);

/// <summary>The two production states, named so they cannot be mistyped into each other.</summary>
public static class DeliveryReadProdStates
{
    /// <summary>A human decided to defer it. <b>Not a failure and not a gap.</b></summary>
    public const string DeferredByOwner = "DEFERRED_BY_OWNER";

    /// <summary>No deployment has been attempted. <b>Not a failure.</b></summary>
    public const string NotStarted = "NOT_STARTED";

    /// <summary>No authority answers the production readiness question.</summary>
    public const string Unknown = "UNKNOWN";
}

/// <summary>One build the authority records.</summary>
public sealed record DeliveryReadBuild(
    string BuildId,
    string ArtifactId,
    DeliveryAuthorityClass Authority);

/// <summary>One artifact the authority holds, with its immutable binding.</summary>
public sealed record DeliveryReadArtifact(
    string ArtifactId,
    string BuildId,
    string ContentDigest,
    long SizeBytes,
    string Lifecycle,
    string PublishedAt,
    DeliveryAuthorityClass Authority);

/// <summary>
/// The `ArtifactId` → exactly-one-payload binding, published because it is the control that survives
/// deleting the payload. A projection that omitted it would hide the property a consumer most needs.
/// </summary>
public sealed record DeliveryReadArtifactIdentity(
    string ArtifactId,
    string ContentDigest,
    string State,
    int PublishAttempts,
    string FirstAcceptedAt,
    DeliveryAuthorityClass Authority);

/// <summary>One release, with the ref it governs and the artifacts it carries.</summary>
public sealed record DeliveryReadRelease(
    string ReleaseId,
    string UnitId,
    string Version,
    string Lifecycle,
    string BuildId,
    string BundleId,
    string ReleaseRefName,
    IReadOnlyList<string> SourceCommits,
    IReadOnlyList<string> ArtifactIds,
    string RegisteredAt,
    DeliveryAuthorityClass Authority);

/// <summary>
/// One deployment attempt. <b>Environment is carried per item</b> — a release is not "in an
/// environment"; a specific attempt is.
/// </summary>
public sealed record DeliveryReadDeployment(
    string DeploymentId,
    string ReleaseId,
    string Environment,
    string Attempt,
    string State,
    string LastTransition,
    string LastOccurredAt,
    DeliveryAuthorityClass Authority);

/// <summary>
/// An environment Delivery knows about, derived from the attempts that name it. <b>Not a runtime
/// observation.</b>
/// </summary>
public sealed record DeliveryReadEnvironment(
    string Environment,
    string Role,
    int AttemptCount,
    string LastObservedAt,
    DeliveryAuthorityClass Authority);

/// <summary>
/// A verification the authority recorded — certification, promotion gate, smoke. All of it is
/// <b>historical evidence</b>: it states what was measured, at a time, not what is true now.
/// </summary>
public sealed record DeliveryReadVerification(
    string Subject,
    string Kind,
    string Verdict,
    string ObservedAt,
    DeliveryAuthorityClass Authority);

/// <summary>A promotion decision recorded against a release and environment.</summary>
public sealed record DeliveryReadPromotion(
    string ReleaseId,
    string FromEnvironment,
    string ToEnvironment,
    string State,
    string ObservedAt,
    DeliveryAuthorityClass Authority);

/// <summary>Migration state, as the release bundle declares it.</summary>
public sealed record DeliveryReadMigration(
    string ReleaseId,
    string State,
    string Provider,
    IReadOnlyList<string> MigrationIds,
    DeliveryAuthorityClass Authority);

/// <summary>
/// The deterministic backup obligation. <b>No backup state is claimed</b> — the authority records
/// the requirement, and there is no source that reports whether a backup ran. That absence is
/// stated, not filled.
/// </summary>
public sealed record DeliveryReadBackup(
    string State,
    string Detail,
    DeliveryAuthorityClass Authority);

/// <summary>Rollback state for a release.</summary>
public sealed record DeliveryReadRollback(
    string ReleaseId,
    string State,
    string PreviousReleaseId,
    bool CrossesMigrationBoundary,
    bool Rehearsed,
    string Basis,
    DeliveryAuthorityClass Authority);

/// <summary>
/// The release-reference protection position, <b>including the deferral</b>.
///
/// <para>
/// <see cref="ServerSideVerified"/> is published as it stands, which today is <c>false</c>, and
/// <see cref="Mode"/> names the DEV/TEST compensating control. Publishing a deferral as though it
/// were a failure — or as though it were protection — would be the collapse TASK 5 forbids.
/// </para>
/// </summary>
public sealed record DeliveryReadReferenceProtection(
    string Mode,
    bool ServerSideVerified,
    string AllowedEnvironmentScope,
    string CredentialRotationStatus,
    string DecisionReference,
    string DecisionAuthority,
    DeliveryAuthorityClass Authority);

/// <summary>One lineage edge. Only edges the authority actually holds are published.</summary>
public sealed record DeliveryReadLineageEdge(
    string From,
    string To,
    string Kind,
    DeliveryAuthorityClass Authority);

/// <summary>
/// <b>An authoritative edge that is absent, stated rather than repaired.</b>
///
/// <para>
/// This is the type that keeps the projection honest. A consumer that finds a gap knows the lineage
/// is incomplete; a consumer given a plausible reconstruction would not. Nothing here is ever
/// inferred from prose.
/// </para>
/// </summary>
public sealed record DeliveryReadGap(
    string Subject,
    string Kind,
    string Detail);

/// <summary>
/// How a fact came to be known. <b>These are never collapsed into one status.</b>
///
/// <para>
/// The vocabulary is the W10.0 classification model, carried forward. Its value is that a consumer
/// can distinguish "the owner asserts this" from "the owner measured this once" from "a runtime
/// observed this" from "nobody can answer it" — four different questions that a single
/// <c>status</c> field would answer as one.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeliveryAuthorityClass>))]
public enum DeliveryAuthorityClass
{
    /// <summary>The Delivery authority itself asserts this. Releases, artifacts, bindings, plans.</summary>
    Authoritative,

    /// <summary>Computed by this projection from authoritative facts. Environment attempt counts.</summary>
    Derived,

    /// <summary>Observed from a running system. <b>Nothing in this projection is this class today</b> —
    /// no runtime observation source exists, and the absence is deliberate rather than an oversight.</summary>
    ObservedRuntime,

    /// <summary>Recorded once, at a time. Certification verdicts, promotion decisions, verifications.
    /// <b>Historical evidence is not current state.</b></summary>
    HistoricalEvidence,

    /// <summary>No authority answers this. Published so the consumer can tell it apart from zero.</summary>
    Unavailable
}

/// <summary>The contract's identity, in one place so a producer and a consumer cannot disagree.</summary>
public static class DeliveryReadContract
{
    /// <summary>
    /// The versioned contract identifier. A consumer refuses anything it does not recognise; this
    /// projection refuses to emit anything else.
    /// </summary>
    public const string SchemaVersion = "nexus.delivery-read-model.v1";

    /// <summary>Who owns the facts. Named so a consumer never has to infer it.</summary>
    public const string Authority = "Platform Delivery";

    /// <summary>
    /// The published file name. A constant, not a convention a caller re-invents.
    /// </summary>
    public const string FileName = "delivery-read-model.v1.json";
}
