namespace Nexus.Delivery.Contracts;

/// <summary>
/// What was <b>observed</b>, as opposed to what is asserted, at the moment a transition is
/// evaluated.
///
/// <para>
/// <b>Every member is nullable, and that is the design.</b> The distinction between "not supplied"
/// and "observed false" is the difference between <see cref="DeploymentRefusalReason.EvidenceIncomplete"/>
/// and a specific refusal, and collapsing it would let a missing observation read as a healthy one.
/// W9.0 measured that the estate has no unit that reports its own build id, digest or source commit —
/// so at present this evidence cannot be filled from a running process at all, and the machine must
/// say so rather than proceed.
/// </para>
///
/// <para>
/// A refusal on absent evidence is deliberate and mirrors the W8F rule that a suite which cannot run
/// is reported as <c>NOT_RUN</c> and never as a pass.
/// </para>
/// </summary>
public sealed record DeploymentGateEvidence
{
    /// <summary>From the build. Null when not supplied.</summary>
    public bool? WorkingTreeIsDirty { get; init; }

    /// <summary>Whether the source ref is a protected ref. Null when the estate cannot yet answer.</summary>
    public bool? ReleaseRefIsProtected { get; init; }

    /// <summary>Observed readiness in the target. Null when nothing observed it.</summary>
    public bool? ReadinessObserved { get; init; }

    /// <summary>
    /// The smoke observation: the process this run started was still alive after the probes, and the probes
    /// answered as required. <b>Load-bearing, not informational.</b>
    ///
    /// <para>
    /// <b>Why readiness alone is not enough, measured rather than argued.</b> A readiness probe answers the
    /// question "is <i>something</i> serving this address", not "is the process <i>I</i> started serving it".
    /// A W9.4 negative-control run started a second instance while a verified instance already held the
    /// port; the new process failed to bind and exited, the probe was answered by the older process, and the
    /// run reported a healthy deployment it had not established. The driver captured the discriminating
    /// facts — <c>logLinesCaptured=0</c>, <c>processSurvivedSmoke=false</c> — and consulted neither.
    /// </para>
    ///
    /// <para>
    /// Three-valued for the same reason <see cref="ReadinessObserved"/> is: <see langword="null"/> means the
    /// observation was never taken, which is a refusal, not a pass. A boolean would collapse "not observed"
    /// into "observed false" and lose the distinction that matters to a reader.
    /// </para>
    /// </summary>
    public bool? SmokeObserved { get; init; }

    /// <summary>Digests observed in the target, per unit. Null when the target was not inspected.</summary>
    public IReadOnlyDictionary<DeploymentUnitId, ArtifactDigest>? ObservedDigests { get; init; }

    /// <summary>The target database's applied schema relative to the bundle's migration set.</summary>
    public MigrationCompatibility? MigrationCompatibility { get; init; }

    /// <summary>Whether every configuration key the bundle declares as varying is satisfied in the target.</summary>
    public bool? ConfigurationSchemaSatisfied { get; init; }

    /// <summary>The authorization supplied for this transition, if any.</summary>
    public DeploymentAuthorization? Authorization { get; init; }

    /// <summary>The bundle currently deployed in the target, when a rollback is being considered.</summary>
    public ReleaseBundle? PreviousBundle { get; init; }

    /// <summary>Whether the registry holds the previous bundle, so a rollback could actually be performed.</summary>
    public bool? PreviousBundleAvailable { get; init; }

    /// <summary>
    /// Whether the rollback being requested has been performed before in a non-production environment
    /// (rule R-7.1: a rollback is proven in ENV-TEST before it is relied on in ENV-PROD). A separate
    /// member from <see cref="PreviousBundleAvailable"/> on purpose — availability is a fact about the
    /// registry, rehearsal is a fact about the team, and collapsing them would let the presence of a
    /// stored bundle read as evidence that anyone has ever rolled back.
    /// </summary>
    public bool? RollbackRehearsed { get; init; }

    /// <summary>Free-text reason, required by quarantine and rollback. Recorded in lineage, never a value.</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Which mechanism protects the release-tag namespace, and the facts it rests on.
    ///
    /// <para>
    /// <b>Separate from <see cref="ReleaseRefIsProtected"/>, and neither replaces the other.</b>
    /// <c>ReleaseRefIsProtected</c> answers rule 2's question — is there <i>any</i> refusal in place against an
    /// unauthorized write — and remains exactly as it was. This member answers the question that boolean
    /// cannot: <b>which</b> mechanism is relied on, and whether it is permitted to cover the environment being
    /// deployed into. The Owner-approved C-2 compensating control is a DEV/TEST-only substitution, so a
    /// promotion to ENV-PROD that rested on it would be resting on a control that does not extend there — and
    /// a boolean set to <c>true</c> cannot express that distinction.
    /// </para>
    ///
    /// <para>
    /// Unrecorded by default, which refuses. A caller that has not said how the reference is protected has not
    /// established that it is.
    /// </para>
    /// </summary>
    public ReleaseReferenceProtectionEvidence ReleaseReferenceProtection { get; init; }
        = ReleaseReferenceProtectionEvidence.Unrecorded;

    public static DeploymentGateEvidence Empty { get; } = new();
}
