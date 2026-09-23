using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// The states a <b>release</b> occupies, up to and including readiness for DEV.
///
/// <para>
/// <b>This is not <see cref="PromotionState"/>, and the difference is the point.</b> A promotion state is
/// per (bundle, environment) — a bundle that is <see cref="PromotionState.Live"/> in ENV-PROD is still
/// <see cref="PromotionState.VerifiedTest"/> elsewhere. A release lifecycle state is per <b>release</b> and
/// is <b>environment-free</b>: it says what has been established about the release itself, before any
/// environment is involved. W9.3 implements only the states required to reach DEV readiness; the deployment
/// states that follow live on the promotion machine, and W9.4 owns them.
/// </para>
///
/// <para>
/// <b>Terminal-looking states are deliberate.</b> <see cref="Refused"/> and <see cref="Withdrawn"/> exist so
/// that a release which must not proceed has somewhere to be, rather than being left in the state it failed
/// from. A lifecycle whose only outcomes are "progressing" and "broken" cannot distinguish a release that
/// was refused for a stated reason from one that was never finished.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseLifecycleState
{
    /// <summary>A certified build exists for the release's inputs. Nothing is registered yet.</summary>
    BuildCertified,

    /// <summary>The certified artifact is published in the immutable store and its bytes hash to the digest the build recorded.</summary>
    ArtifactRegistered,

    /// <summary>A Release Bundle is assembled but not yet certified. It may be inspected and refined; nothing may be promoted from it.</summary>
    ReleaseDraft,

    /// <summary>The release certification gate passed every required condition.</summary>
    ReleaseCertified,

    /// <summary>
    /// Eligible for DEV deployment, with nothing outstanding. Reaching this state means every gate passed
    /// and no security action is pending.
    /// </summary>
    ReadyForDev,

    /// <summary>
    /// <b>Eligible for local certification, and NOT eligible for external publication or deployment.</b>
    ///
    /// <para>
    /// This state exists because collapsing it into <see cref="Refused"/> or into
    /// <see cref="ReadyForDev"/> would both be lies. Every technical condition is met; one or more
    /// security actions (credential rotation, server-side release-reference protection) are outstanding.
    /// Reporting a generic failure would hide that the release is complete and waiting on a human act;
    /// reporting plain readiness would make a deployable-looking release out of one that must not be
    /// deployed. It is named as its own state so that a reader sees exactly what is missing.
    /// </para>
    /// </summary>
    ReadyForDevPendingSecurityAction,

    /// <summary>
    /// A required condition failed, with a typed reason. Not releasable. A changed artifact always
    /// produces a NEW release rather than moving this one back.
    /// </summary>
    Refused,

    /// <summary>Withdrawn by an Owner act. Terminal; no transition leaves it.</summary>
    Withdrawn
}

/// <summary>
/// The transitions of the release lifecycle. As with <see cref="DeploymentTransition"/>, this enum is also
/// the vocabulary a release lineage record would use, so a transition cannot be recorded under a name that
/// is not a transition.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseTransition
{
    /// <summary>Anchored at <see cref="ReleaseLifecycleState.BuildCertified"/>. A factory, not a transition: a certified build has no prior release state.</summary>
    CertifyBuild,

    /// <summary>The store must accept the certified artifact, and the bytes must hash to the recorded digest.</summary>
    RegisterArtifact,

    /// <summary>Assembles a Release Bundle from the registered artifact. Requires every member TASK 2 names to be present.</summary>
    DraftRelease,

    /// <summary>Runs the release certification gate.</summary>
    CertifyRelease,

    /// <summary>Declares DEV readiness. Refused when any security action is outstanding.</summary>
    DeclareReadyForDev,

    /// <summary>
    /// Declares DEV readiness with a security action outstanding. Refused when <b>none</b> is outstanding,
    /// so this state cannot be entered as a shortcut around <see cref="DeclareReadyForDev"/>.
    /// </summary>
    DeclareReadyForDevPendingSecurityAction,

    /// <summary>Refuses the release with a typed reason. Permitted without authorization: a refusal is an observation, not a decision.</summary>
    Refuse,

    /// <summary>Owner-reserved withdrawal.</summary>
    Withdraw
}

/// <summary>Why a release could not advance. Every member is a distinct, actionable deficiency.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseRefusalReason
{
    None = 0,

    /// <summary>Evidence the gate needed was not supplied. Null is "not observed", never "fine".</summary>
    EvidenceIncomplete,

    /// <summary>The transition is not legal from the state the release occupies.</summary>
    IllegalTransition,

    /// <summary>The release record's schema version is outside what this contract understands.</summary>
    ReleaseSchemaUnsupported,

    /// <summary>The certified build's artifact is not present in the store.</summary>
    ArtifactNotRegistered,

    /// <summary>The stored bytes do not hash to the digest the build manifest recorded. A corruption finding, not a caller error.</summary>
    ArtifactHashMismatch,

    /// <summary>The build the release names was not certified, so there is no certified artifact to release.</summary>
    BuildNotCertified,

    /// <summary>The release bundle is missing a member TASK 2 requires.</summary>
    ReleaseBundleIncomplete,

    /// <summary>The release reference is absent, or is not a governed annotated release reference.</summary>
    ReleaseRefNotGoverned,

    /// <summary>The release reference exists but points at different source, or does not contain the build commit.</summary>
    ReleaseRefDoesNotMatchSource,

    /// <summary>Server-side release-reference protection could not be verified, and is not assumed.</summary>
    ReleaseRefServerSideProtectionUnverified,

    /// <summary>An active release input carries a secret finding. Certification blocks; the value is never reproduced.</summary>
    SecretScanFindingsInActiveInput,

    /// <summary>The secret scan could not complete, so the input set is not established as clean.</summary>
    SecretScanIncomplete,

    /// <summary>The artifact carries an environment-specific value, so reaching another environment would require a rebuild.</summary>
    EnvironmentSpecificValueInArtifact,

    /// <summary>A configuration key or secret reference is not name-shaped, so a value may have been recorded where a name belongs.</summary>
    ConfigurationValueInRelease,

    /// <summary>The reproducibility verdict is not established.</summary>
    ReproducibilityNotEstablished,

    /// <summary>The required tests did not pass.</summary>
    TestsNotPassed,

    /// <summary>Whether a database migration applies is unknown, and the release must not be promoted on an unmeasured migration risk.</summary>
    MigrationStateUnknown,

    /// <summary>A migration applies and the target must be backed up before the release; the requirement is recorded but unsatisfied.</summary>
    MigrationBackupNotEstablished,

    /// <summary>No previous accepted release exists to roll back to, and the release is asserted to have one.</summary>
    RollbackReferenceFabricated,

    /// <summary>The health definition is absent, so no promoter can read a readiness signal.</summary>
    HealthDefinitionMissing,

    /// <summary>A dependency manifest could not be resolved and the absence was not recorded.</summary>
    DependencyManifestMissing,

    /// <summary>A pinned contract version is incompatible with what the release requires.</summary>
    ContractCompatibilityUnacceptable,

    /// <summary>An Owner authorization is required for this act and was not supplied.</summary>
    OwnerAuthorizationRequired,

    /// <summary>
    /// Unqualified DEV readiness was declared while a security action is outstanding. The release must
    /// declare the pending state instead, so that nothing deployable-looking is produced from it.
    /// </summary>
    SecurityActionOutstanding,

    /// <summary>
    /// The pending-security-action state was declared while <b>no</b> security action is outstanding.
    /// Refused so that the state cannot be entered as a shortcut past <see cref="DeclareReadyForDev"/>.
    /// </summary>
    NoOutstandingSecurityAction,

    /// <summary>An act was requested on a release that was withdrawn. Withdrawal is terminal; a new release is the remedy.</summary>
    ReleaseIsWithdrawn
}
