namespace Nexus.Delivery.Contracts;

/// <summary>
/// Everything the release lifecycle machine is allowed to know.
///
/// <para>
/// <b>Every observation is nullable, and null is not "fine".</b> This is the estate's standing rule — a
/// thing that could not be checked is reported as <c>NOT_RUN</c> and never as a pass — applied to the
/// release gates. A non-nullable <c>bool</c> defaulting to <c>false</c> would refuse for the wrong reason,
/// and defaulting to <c>true</c> would pass for no reason; a nullable member forces the caller to have an
/// answer, and the machine refuses with <see cref="ReleaseRefusalReason.EvidenceIncomplete"/> when it
/// does not.
/// </para>
///
/// <para>
/// <b>Two of these are security actions rather than technical conditions.</b>
/// <see cref="CredentialRotationConfirmed"/> and <see cref="ReleaseRefServerSideProtectionVerified"/> are
/// facts about the world that no local process can establish by inspection. They are carried separately
/// from the technical gates because they route a release to
/// <see cref="ReleaseLifecycleState.ReadyForDevPendingSecurityAction"/> rather than refusing it outright:
/// the release is complete and waiting on a human act, which is a different thing from being broken.
/// </para>
///
/// <para>
/// Init-only properties with no constructor, matching <see cref="DeploymentGateEvidence"/>: an evidence bag
/// is a set of observations a caller fills in, and a test that needs to break exactly one of them should be
/// able to say so without restating the other fourteen.
/// </para>
/// </summary>
public sealed record ReleaseGateEvidence
{
    public bool? BuildIsCertified { get; init; }

    public bool? SourceLineageComplete { get; init; }

    /// <summary>The stored bytes hash to the digest the build manifest recorded.</summary>
    public bool? ArtifactHashMatchesManifest { get; init; }

    public bool? BundleIsComplete { get; init; }

    /// <summary>The release reference is an annotated, governed tag whose recorded identity matches this release.</summary>
    public bool? ReleaseRefGoverned { get; init; }

    public bool? DependencyManifestAvailable { get; init; }

    public bool? ContractCompatibilityAcceptable { get; init; }

    public bool? MigrationStateKnown { get; init; }

    /// <summary>
    /// Whether the backup requirement is <b>established</b> for this release — known and recorded.
    ///
    /// <para>
    /// It does <b>not</b> mean a backup has been taken. Taking one is a deployment act and belongs to
    /// W9.4; a release stage that claimed to have taken one would be claiming an action it cannot perform.
    /// What a release can establish is that the requirement is known, so that the stage which deploys
    /// cannot proceed without noticing it.
    /// </para>
    /// </summary>
    public bool? MigrationBackupEstablished { get; init; }

    public bool? ConfigurationSchemaKnown { get; init; }

    public bool? HealthDefinitionPresent { get; init; }

    public bool? RollbackStateKnown { get; init; }

    public bool? SecretScanPassed { get; init; }

    /// <summary>No environment endpoint and no secret value is baked into the artifact or carried by the release.</summary>
    public bool? ConfigurationBoundaryClean { get; init; }

    public bool? ReproducibilityEstablished { get; init; }

    public bool? TestsPassed { get; init; }

    /// <summary>
    /// A security action, not a technical condition: whether the W9.0 credential exposure has been rotated
    /// or revoked. <b>Never determined by reading the credential</b> — this is the Human Owner's
    /// confirmation, recorded. Null means unconfirmed, which is the state until someone says otherwise.
    /// </summary>
    public bool? CredentialRotationConfirmed { get; init; }

    /// <summary>
    /// A security action, not a technical condition: whether the remote repository's ruleset protecting the
    /// release-tag namespace has been verified by an authorized check. A local process cannot observe it,
    /// so it is never inferred from a local mechanism being present.
    /// </summary>
    public bool? ReleaseRefServerSideProtectionVerified { get; init; }

    public DeploymentAuthorization? Authorization { get; init; }

    /// <summary>Operator-facing. Must never contain a secret value.</summary>
    public string? Reason { get; init; }

    public static ReleaseGateEvidence Empty { get; } = new();

    /// <summary>True when at least one security action is recorded as outstanding.</summary>
    public bool HasOutstandingSecurityAction
        => CredentialRotationConfirmed == false || ReleaseRefServerSideProtectionVerified == false;

    /// <summary>True when at least one security action could not be observed at all.</summary>
    public bool HasUnobservedSecurityAction
        => CredentialRotationConfirmed is null || ReleaseRefServerSideProtectionVerified is null;
}
