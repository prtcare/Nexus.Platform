using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// Why a promotion precondition was not satisfied. Every refusal carries one of these.
///
/// <para>
/// <b>Distinct from <see cref="DeploymentRefusalReason"/>, and deliberately not merged into it.</b> That
/// vocabulary answers the state machine's question — <i>is this transition legal from this state, given
/// this evidence?</i> — and it is total over <see cref="DeploymentTransition"/>. This vocabulary answers
/// the question that runs <b>before</b> any transition is proposed: <i>has the thing being promoted
/// actually been established?</i> The two are asked at different times by different readers, and the
/// estate already holds the cost of collapsing two judgements into one member: the single
/// <c>ReleaseRefServerSideProtectionVerified</c> boolean that had to report a substituted control as the
/// control it replaced.
/// </para>
///
/// <para>
/// <b>Every member names a requirement that was absent, not a condition that failed.</b> Where both are
/// possible the detail distinguishes them, because "nobody has established this yet" and "this was
/// established and is false" route to different next actions.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PromotionRefusalReason
{
    None = 0,

    /// <summary>The release's own certification is not established — the registry's decode-path verification did not return intact, or the lifecycle is not a promotable one.</summary>
    SourceReleaseNotCertified,

    /// <summary>No deployment of this bundle into the environment the promotion promotes <i>from</i> has been recorded. A promotion from an environment the artifact never reached would promote nothing.</summary>
    SourceDeploymentNotRecorded,

    /// <summary>
    /// A deployment exists in the source environment but no verification of it does. This is rule R-4 made
    /// structural: an artifact reaches an environment only from the one immediately before it, and it
    /// reaches it <i>proven</i>, not merely present.
    /// </summary>
    SourceVerificationNotComplete,

    /// <summary>
    /// Whether the release's rollback remedy has been exercised is unrecorded, or is recorded as not
    /// exercised. Three-valued upstream for the estate's standing reason: <see langword="null"/> is
    /// "nobody has said", which is not the same fact as "it was tried and it failed", and neither is a pass.
    /// </summary>
    RollbackCapabilityNotProven,

    /// <summary>The pre-deployment release-reference control did not verify, so the certified chain has no intact governed pointer.</summary>
    ReleaseReferenceNotIntact,

    /// <summary>The stored bytes are not established as the certified bytes.</summary>
    ArtifactIntegrityNotIntact,

    /// <summary>The governed release plan does not authorize the target environment — the C-2 boundary, or an unconfirmed rotation.</summary>
    SecurityPlanDoesNotPermitTarget,

    /// <summary>
    /// The register of defects that block deployment was not declared, or declares one that is unresolved.
    /// <b>Not declared is a refusal</b>: a party that has not stated what blocks deployment has not
    /// established that nothing does.
    /// </summary>
    DeploymentBlockingDefectOutstanding,

    /// <summary>The target environment's descriptor did not validate, so the machine the promotion would touch is not described.</summary>
    TargetEnvironmentDefinitionNotEstablished,

    /// <summary>
    /// The target is not later in promotion order than the source, so this is not a promotion. Refused rather
    /// than treated as a no-op: a step filed as a promotion that moves nothing would put a promotion record in
    /// the lineage for an act that did not happen.
    /// </summary>
    TargetIsNotAPromotion
}

/// <summary>
/// One declared defect that blocks deployment, and whether it is discharged.
///
/// <para>
/// <b>Why a record rather than a boolean.</b> A boolean saying "nothing blocks this deployment" is
/// unfalsifiable — it carries no subject a reader can check. A register names each blocking defect and its
/// state, so a reviewer can ask <i>"is that one actually resolved?"</i> of a specific claim. The estate's
/// debt registers (<c>W8_W9_DEBT_CHECK.md</c>, <c>W9_1_DEBT_REGISTER.md</c>) are the same shape for the same
/// reason.
/// </para>
/// </summary>
/// <param name="DefectId">The identifier the register uses, e.g. <c>W9-DEBT-01</c>. Must be non-blank.</param>
/// <param name="Summary">One line naming what is blocked. Never a credential value — this reaches evidence.</param>
/// <param name="IsResolved">Whether the defect is discharged. A defect declared and still open refuses the promotion.</param>
/// <param name="Resolution">How it was discharged. Required when <paramref name="IsResolved"/> is true, so
/// "resolved" is a statement about an act rather than an assertion.</param>
public sealed record DeploymentBlockingDefect(
    string DefectId,
    string Summary,
    bool IsResolved,
    string? Resolution)
{
    /// <summary>A defect ID is an identifier, so it must read as one.</summary>
    public bool IsWellFormed =>
        !string.IsNullOrWhiteSpace(DefectId)
        && !string.IsNullOrWhiteSpace(Summary);

    /// <summary>
    /// Whether this defect is discharged <b>with a stated act</b>. A defect marked resolved with no
    /// resolution recorded is treated as <b>unresolved</b>: the register's whole purpose is that a claim can
    /// be checked, and "resolved" with nothing beside it cannot be.
    /// </summary>
    public bool IsDischarged =>
        IsResolved && !string.IsNullOrWhiteSpace(Resolution);
}

/// <summary>
/// The register of defects that block a deployment, as declared by the party entitled to declare it.
///
/// <para>
/// <b>There is no default register that passes.</b> <see cref="NotDeclared"/> is the initial state and it
/// <b>refuses</b> (see <see cref="PromotionGovernance.Evaluate"/>), which is the same shape as
/// <see cref="ReleaseReferenceProtectionEvidence.Unrecorded"/> and for the same reason: a check whose
/// input defaults to "everything is fine" is a check that cannot fail, and this estate keeps re-finding
/// that defect in new forms. Supplying <see cref="Empty"/> is therefore a deliberate act — the caller
/// states that it has enumerated the register and it holds nothing — and it is distinguishable in evidence
/// from never having stated anything at all.
/// </para>
/// </summary>
public sealed record DeploymentBlockingDefectRegister
{
    private DeploymentBlockingDefectRegister(bool isDeclared, IReadOnlyList<DeploymentBlockingDefect> defects)
    {
        IsDeclared = isDeclared;
        Defects = defects;
    }

    /// <summary>Whether a party stated the register. False means nobody has.</summary>
    public bool IsDeclared { get; }

    public IReadOnlyList<DeploymentBlockingDefect> Defects { get; }

    /// <summary>Nothing stated. Refuses.</summary>
    public static DeploymentBlockingDefectRegister NotDeclared { get; } = new(false, []);

    /// <summary>A declared register holding no blocking defect.</summary>
    public static DeploymentBlockingDefectRegister Empty { get; } = new(true, []);

    /// <summary>A declared register. An empty sequence is a declaration that the register is empty, which is why it is allowed here and not by default.</summary>
    public static DeploymentBlockingDefectRegister Declared(IEnumerable<DeploymentBlockingDefect> defects)
    {
        ArgumentNullException.ThrowIfNull(defects);
        return new(true, [.. defects]);
    }

    /// <summary>Every declared defect that is not discharged with a stated act, in declaration order.</summary>
    public IReadOnlyList<DeploymentBlockingDefect> Unresolved =>
        [.. Defects.Where(d => !d.IsDischarged)];
}

/// <summary>
/// Everything the promotion precondition gate needs, as <b>observed</b> rather than asserted.
///
/// <para>
/// <b>Every member is nullable or has a refusing default, and that is the design.</b> The estate's rule is
/// that absence of evidence is a refusal and never an assumption of health; a member that defaulted to
/// <see langword="true"/> would let a caller that supplied nothing receive a promotion. The one member with
/// a non-null default, <see cref="BlockingDefects"/>, defaults to the <i>refusing</i> state rather than the
/// passing one, for the same reason.
/// </para>
/// </summary>
public sealed record PromotionGovernanceRequest
{
    /// <summary>The environment being promoted into. Required.</summary>
    public required DeploymentEnvironmentId TargetEnvironment { get; init; }

    /// <summary>The state the bundle occupies before the promotion. Required.</summary>
    public required PromotionState CurrentState { get; init; }

    /// <summary>Whether the registry's decode-path verification returned intact, and the lifecycle is promotable.</summary>
    public bool? SourceReleaseCertified { get; init; }

    /// <summary>The lifecycle the registry reports, for the detail line. Never the decision.</summary>
    public ReleaseLifecycleState? SourceReleaseLifecycle { get; init; }

    /// <summary>
    /// Lineage ids of the records that deployed this bundle into the environment the promotion promotes
    /// from. Null when the ledger was not read; empty when it was read and holds none.
    /// </summary>
    public IReadOnlyList<string>? SourceDeploymentLineageIds { get; init; }

    /// <summary>Lineage ids of the records that verified this bundle in that same environment.</summary>
    public IReadOnlyList<string>? SourceVerificationLineageIds { get; init; }

    /// <summary>Whether the rollback remedy has been exercised. Null when unrecorded.</summary>
    public bool? RollbackCapabilityProven { get; init; }

    /// <summary>Whether the pre-deployment release-reference control verified.</summary>
    public bool? ReleaseReferenceIntact { get; init; }

    /// <summary>Whether the bytes in hand are established as the certified bytes.</summary>
    public bool? ArtifactIntegrityIntact { get; init; }

    /// <summary>Whether the governed release plan's security readiness covers <see cref="TargetEnvironment"/>.</summary>
    public bool? SecurityPlanPermitsTarget { get; init; }

    /// <summary>The declared register of deployment-blocking defects. Defaults to the refusing state.</summary>
    public DeploymentBlockingDefectRegister BlockingDefects { get; init; } = DeploymentBlockingDefectRegister.NotDeclared;

    /// <summary>Whether the target environment's descriptor validated.</summary>
    public bool? TargetEnvironmentDefinitionValid { get; init; }

    /// <summary>The environment the promotion moves <i>from</i>, when the caller knows it. Used only to refuse a promotion that promotes nothing.</summary>
    public DeploymentEnvironmentId? SourceEnvironment { get; init; }
}

/// <summary>
/// The gate's answer: whether the promotion may be proposed to the state machine at all, and every
/// requirement that was absent.
///
/// <para>
/// <b>An allowance here is not an authorization.</b> This gate establishes preconditions; the transition
/// itself is still <see cref="DeploymentStateMachine"/>'s decision and is still recorded by
/// <c>DeploymentPromoter</c>. Two gates in series, each able to refuse, is the point — the estate's
/// security gate and deployment gate ask the same protection question twice on purpose, and the reasoning
/// that one check makes the other redundant is how the stronger of two controls disappears during a
/// refactor.
/// </para>
/// </summary>
/// <param name="IsAllowed">True only when every requirement is established and satisfied.</param>
/// <param name="RefusalReasons">Every absent or failed requirement, in evaluation order. Never truncated to the first.</param>
/// <param name="Detail">Operator-facing, one line per reason. <b>Never a credential value</b> — this text reaches deployment logs and evidence.</param>
public sealed record PromotionGovernanceDecision(
    bool IsAllowed,
    IReadOnlyList<PromotionRefusalReason> RefusalReasons,
    IReadOnlyList<string> Detail)
{
    public bool IsRefused => !IsAllowed;

    public static PromotionGovernanceDecision Allowed(IReadOnlyList<string> detail)
        => new(true, [PromotionRefusalReason.None], detail);

    public static PromotionGovernanceDecision Refused(
        IReadOnlyList<PromotionRefusalReason> reasons,
        IReadOnlyList<string> detail)
        => new(false, reasons, detail);

    public override string ToString()
        => IsAllowed ? "PROMOTION_PRECONDITIONS_SATISFIED" : string.Join(", ", RefusalReasons);
}

/// <summary>
/// <b>The deterministic promotion precondition gate.</b> A pure function of the request: no clock, no I/O,
/// no environment, so two hosts holding the same observations reach the same decision.
///
/// <para>
/// <b>What it is for, and what it is not.</b> The directive that created it requires nine conditions
/// before an artifact already proven in ENV-DEV may be promoted to ENV-TEST. Four of the nine were already
/// gated by <see cref="DeploymentStateMachine.GatePromotion"/> — the live observations of the target. The
/// other five are facts about <i>what has been established so far</i>: that the source environment's
/// deployment and verification are recorded, that a rollback remedy exists and has been exercised, that
/// nothing is declared to block the deployment, and that the target machine is described at all. Those
/// facts belong to no transition's evidence bag, because they are not observations of the target. Asking
/// them here, before a transition is proposed, is what keeps <see cref="DeploymentGateEvidence"/> from
/// growing a member for every governance question the estate ever asks.
/// </para>
///
/// <para>
/// <b>The nine conditions, and where each is decided.</b>
/// </para>
/// <list type="number">
/// <item><description>source <c>ReleaseId</c> certified → <see cref="SourceReleaseCertified"/></description></item>
/// <item><description>source deployment recorded → <see cref="SourceDeploymentLineageIds"/></description></item>
/// <item><description>source verification complete → <see cref="SourceVerificationLineageIds"/></description></item>
/// <item><description>rollback capability proven → <see cref="RollbackCapabilityProven"/></description></item>
/// <item><description>release reference intact → <see cref="ReleaseReferenceIntact"/></description></item>
/// <item><description>artifact integrity intact → <see cref="ArtifactIntegrityIntact"/></description></item>
/// <item><description>security plan allows the target → <see cref="SecurityPlanPermitsTarget"/></description></item>
/// <item><description>no unresolved deployment-blocking defect → <see cref="BlockingDefects"/></description></item>
/// <item><description>target environment definition valid → <see cref="TargetEnvironmentDefinitionValid"/></description></item>
/// </list>
///
/// <para>
/// <b>It does not authorize.</b> Nothing in this estate's delivery path is authorized by a party that is
/// not the Owner or a mechanism the Owner ratified; this gate can only <i>refuse</i>. An allowance from it
/// means the preconditions are established, and the promotion still has to pass the state machine and be
/// recorded by the promoter. "AI must not authorize promotion" is satisfied structurally: there is no code
/// path in which this decision writes a lineage record, moves a ref, or changes a lifecycle.
/// </para>
/// </summary>
public static class PromotionGovernance
{
    /// <summary>
    /// Evaluates the preconditions. <b>Every</b> absent or failed requirement is reported, not just the
    /// first — a reader who fixes one refusal at a time re-runs the gate once per defect, and the estate has
    /// paid for that pattern already.
    /// </summary>
    public static PromotionGovernanceDecision Evaluate(PromotionGovernanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reasons = new List<PromotionRefusalReason>();
        var detail = new List<string>();

        // ---- it must actually be a promotion ------------------------------------------------------
        if (request.SourceEnvironment is not null)
        {
            var source = DeploymentEnvironment.For(request.SourceEnvironment);
            var target = DeploymentEnvironment.For(request.TargetEnvironment);

            if (target.PromotionOrder <= source.PromotionOrder)
            {
                reasons.Add(PromotionRefusalReason.TargetIsNotAPromotion);
                detail.Add(
                    $"TARGET_IS_NOT_A_PROMOTION: the artifact is in {source.EnvironmentId} "
                    + $"(order {source.PromotionOrder}) and the target is {target.EnvironmentId} "
                    + $"(order {target.PromotionOrder}). Rule R-4 promotes only to a later environment.");
            }
        }

        // ---- 1. the source release is certified ----------------------------------------------------
        AddThreeValued(
            reasons, detail, request.SourceReleaseCertified,
            PromotionRefusalReason.SourceReleaseNotCertified,
            "SOURCE_RELEASE_NOT_CERTIFIED",
            $"the release in hand is not established as certified"
            + (request.SourceReleaseLifecycle is null ? "." : $" (registry lifecycle '{request.SourceReleaseLifecycle}')."));

        // ---- 2. the source environment holds a recorded deployment of this bundle -------------------
        AddNullIsAbsence(
            reasons, detail, request.SourceDeploymentLineageIds,
            PromotionRefusalReason.SourceDeploymentNotRecorded,
            "SOURCE_DEPLOYMENT_NOT_RECORDED",
            $"no deployment of this bundle into {request.SourceEnvironment?.Value ?? "the source environment"} "
            + "is recorded, so there is nothing proven to promote.");

        // ---- 3. and a recorded verification of it --------------------------------------------------
        AddNullIsAbsence(
            reasons, detail, request.SourceVerificationLineageIds,
            PromotionRefusalReason.SourceVerificationNotComplete,
            "SOURCE_VERIFICATION_NOT_COMPLETE",
            $"a deployment is recorded but no verification of it is, so the artifact is present in "
            + $"{request.SourceEnvironment?.Value ?? "the source environment"} and not proven there.");

        // ---- 4. a rollback remedy exists and has been exercised ------------------------------------
        AddThreeValued(
            reasons, detail, request.RollbackCapabilityProven,
            PromotionRefusalReason.RollbackCapabilityNotProven,
            "ROLLBACK_CAPABILITY_NOT_PROVEN",
            "whether this release's rollback remedy has been exercised is unrecorded; an "
            + "unexercised remedy is not a capability.");

        // ---- 5. the certified chain still has its governed pointer ---------------------------------
        AddThreeValued(
            reasons, detail, request.ReleaseReferenceIntact,
            PromotionRefusalReason.ReleaseReferenceNotIntact,
            "RELEASE_REFERENCE_NOT_INTACT",
            "the pre-deployment release-reference control did not verify, so the certified chain has no "
            + "intact governed pointer.");

        // ---- 6. the bytes are the certified bytes --------------------------------------------------
        AddThreeValued(
            reasons, detail, request.ArtifactIntegrityIntact,
            PromotionRefusalReason.ArtifactIntegrityNotIntact,
            "ARTIFACT_INTEGRITY_NOT_INTACT",
            "the artifact's bytes are not established as the certified bytes.");

        // ---- 7. the governed plan's security readiness covers the target ---------------------------
        AddThreeValued(
            reasons, detail, request.SecurityPlanPermitsTarget,
            PromotionRefusalReason.SecurityPlanDoesNotPermitTarget,
            "SECURITY_PLAN_DOES_NOT_PERMIT_TARGET",
            $"the governed release plan does not authorize {request.TargetEnvironment}; the recorded "
            + "release-reference protection is a DEV/TEST-scoped substitution and does not extend past it.");

        // ---- 8. nothing is declared to block the deployment ----------------------------------------
        // Absence refuses. This is the member most likely to be supplied vacuously, so it is deliberately
        // NOT a bool?: a caller must either state the register (possibly empty) or not answer at all, and
        // the two are distinguishable in evidence.
        if (!request.BlockingDefects.IsDeclared)
        {
            reasons.Add(PromotionRefusalReason.DeploymentBlockingDefectOutstanding);
            detail.Add(
                "DEPLOYMENT_BLOCKING_DEFECT_REGISTER_NOT_DECLARED: no register of defects blocking this "
                + "deployment has been declared. A party that has not stated what blocks a deployment has not "
                + "established that nothing does.");
        }
        else
        {
            var unresolved = request.BlockingDefects.Unresolved;

            if (unresolved.Count > 0)
            {
                reasons.Add(PromotionRefusalReason.DeploymentBlockingDefectOutstanding);

                foreach (var defect in unresolved)
                {
                    detail.Add(
                        $"DEPLOYMENT_BLOCKING_DEFECT_OUTSTANDING: {defect.DefectId} — {defect.Summary}"
                        + (defect.IsResolved ? " (declared resolved with no resolution recorded)" : string.Empty));
                }
            }
        }

        // ---- 9. the target machine is described ----------------------------------------------------
        AddThreeValued(
            reasons, detail, request.TargetEnvironmentDefinitionValid,
            PromotionRefusalReason.TargetEnvironmentDefinitionNotEstablished,
            "TARGET_ENVIRONMENT_DEFINITION_NOT_ESTABLISHED",
            $"the definition of {request.TargetEnvironment} did not validate, so the machine this promotion "
            + "would touch is not described.");

        return reasons.Count == 0
            ? PromotionGovernanceDecision.Allowed(
                [$"All nine promotion preconditions are established for {request.TargetEnvironment}."])
            : PromotionGovernanceDecision.Refused([.. reasons], [.. detail]);
    }

    /// <summary>
    /// A three-valued observation: <see langword="null"/> is "not established" and is a refusal; an explicit
    /// <see langword="false"/> is a failure and says so in different words. Collapsing the two would report
    /// a missing observation as a defect in the release.
    /// </summary>
    private static void AddThreeValued(
        List<PromotionRefusalReason> reasons,
        List<string> detail,
        bool? observed,
        PromotionRefusalReason reason,
        string code,
        string unestablished)
    {
        if (observed is null)
        {
            reasons.Add(reason);
            detail.Add($"{code}: {unestablished} The observation was not taken, which is not a pass.");
        }
        else if (observed is false)
        {
            reasons.Add(reason);
            detail.Add($"{code}: {unestablished}");
        }
    }

    /// <summary>
    /// A list-valued observation. <see langword="null"/> means the ledger was not read; an empty list means
    /// it was read and holds nothing. <b>Both refuse</b> — for this gate's question the two are the same
    /// answer — but they are carried as different values so the evidence can say which happened.
    /// </summary>
    private static void AddNullIsAbsence(
        List<PromotionRefusalReason> reasons,
        List<string> detail,
        IReadOnlyList<string>? records,
        PromotionRefusalReason reason,
        string code,
        string absence)
    {
        if (records is null)
        {
            reasons.Add(reason);
            detail.Add($"{code}: the ledger was not read, so nothing about this is established.");
            return;
        }

        if (records.Count == 0)
        {
            reasons.Add(reason);
            detail.Add($"{code}: {absence}");
        }
    }

    /// <summary>
    /// The environment a promotion into <paramref name="target"/> must have promoted from, per rule R-4.
    /// Null for the first environment, which is deployed rather than promoted into.
    /// </summary>
    public static DeploymentEnvironmentId? SourceEnvironmentFor(DeploymentEnvironmentId target)
        => DeploymentEnvironment.For(target).PromotedFrom;
}
