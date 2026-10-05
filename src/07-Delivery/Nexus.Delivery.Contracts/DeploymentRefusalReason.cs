using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// Why a transition was refused. Every refusal carries one of these.
///
/// <para>
/// This is the estate's established form. W8E's seven no-bypass controls refused by mechanism, each
/// with a typed reason — six of seven, with the seventh (protected <c>main</c>) refused by nothing,
/// which is why it was recorded as debt rather than counted as a pass. A refusal that cannot name
/// itself cannot be distinguished from a bug, and a gate whose reasons are strings drifts into
/// prose the moment two people write them.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeploymentRefusalReason
{
    None = 0,

    /// <summary>The source working tree has uncommitted changes.</summary>
    SourceWorkingTreeIsDirty,

    /// <summary>The bundle's schema version is outside what this contract understands.</summary>
    BundleSchemaUnsupported,

    /// <summary>The registry refused the write — most often because the bundle id already exists and the registry is write-once.</summary>
    RegistryRefused,

    /// <summary>The unit is declared excluded from deployment and must never be released.</summary>
    UnitExcluded,

    /// <summary>A service unit declares no packaging medium.</summary>
    UnitHasNoDeclaredMedium,

    /// <summary>A service unit declares no readiness signal.</summary>
    UnitHasNoReadinessSignal,

    /// <summary>The environment is not one of the three ratified identifiers.</summary>
    EnvironmentNotRatified,

    /// <summary>No readiness observation is available, so the gate cannot be evaluated. Absence of evidence is a refusal, never an assumption of health.</summary>
    ReadinessNotObserved,

    /// <summary>
    /// No smoke observation is available, or the smoke observation failed. Covers both "never taken" and
    /// "observed false" because both answer the same question the same way: nothing has established that the
    /// process this run started is the thing answering the probe.
    /// </summary>
    SmokeNotObserved,

    /// <summary>The target reports a digest that does not match the bundle's manifest.</summary>
    DigestMismatchInTarget,

    /// <summary>The migration target is ahead of the bundle. Proceeding would run an older application against a newer schema.</summary>
    MigrationSetAhead,

    /// <summary>The migration target diverges from the bundle's set. A manual investigation, not a deployment.</summary>
    MigrationSetDivergent,

    /// <summary>A configuration key the bundle declares as varying is not satisfied in the target.</summary>
    ConfigurationSchemaUnsatisfied,

    /// <summary>The transition is Owner-reserved and no Owner authorization was supplied.</summary>
    OwnerAuthorizationRequired,

    /// <summary>The transition is not legal from the current state.</summary>
    IllegalTransition,

    /// <summary>The bundle is quarantined. Only <see cref="DeploymentTransition.ClearQuarantine"/> may proceed.</summary>
    BundleQuarantined,

    /// <summary>The release source ref is not a protected ref, so the build-once guarantee has no governed source.</summary>
    ReleaseRefNotProtected,

    /// <summary>A secret VALUE was found where only a reference NAME is permitted. Invariant I-7 / L-1.</summary>
    SecretValuePresent,

    /// <summary>The previous bundle needed for a rollback is not available in the registry.</summary>
    PreviousBundleUnavailable,

    /// <summary>A required evidence input was not supplied for this transition.</summary>
    EvidenceIncomplete
}
