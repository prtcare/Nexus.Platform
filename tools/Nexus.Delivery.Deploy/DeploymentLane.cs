using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;

namespace Nexus.Delivery.Deploy;

/// <summary>
/// <b>One environment's role in the delivery path</b>, as a value rather than as a set of literals spread
/// through the driver.
///
/// <para>
/// <b>Why this exists.</b> The W9.4 driver was a DEV driver: <c>DeployToDev</c>, <c>VerifyInDev</c>,
/// <c>PromotionState.DeployedDev</c>, <c>DEV_VERIFICATION.json</c> and <c>ENV-DEV</c> were written into the
/// verbs directly, and <c>EnvDevDescriptor</c> refused any descriptor that named another environment. That
/// was correct for one environment. W9.5 must promote the same certified artifact into a second one, and the
/// estate holds a permanent example of the alternative: two copies of one judgement drift, and the drift is
/// found by a run that refuses for the wrong reason (the W9.2 secret scanner, where "is this a value?" was
/// implemented twice). So the environment-specific facts are gathered into one record and supplied to the
/// shared verbs.
/// </para>
///
/// <para>
/// <b>What is <i>not</i> here, deliberately.</b> The words <c>dev</c> and <c>test</c> appear here and nowhere
/// else in the driver's control flow. The gate that decides a transition
/// (<see cref="DeploymentStateMachine"/>), the type that records it (<c>DeploymentPromoter</c>), the identity
/// (<see cref="DeploymentId"/>) and the evidence address (<c>DeploymentEvidenceTransaction</c>) are all
/// unchanged and all take the environment as data. A lane therefore cannot change <i>whether</i> something is
/// allowed — only <i>which</i> environment the same rules are applied to.
/// </para>
///
/// <para>
/// <b>The transitions are read from the contract, not restated.</b> Each lane's predecessor and successor
/// states are cross-checked against <see cref="DeploymentStateMachine.PredecessorStatesFor"/> and the
/// transition's declared next state at construction, so a lane that disagreed with the machine would throw
/// here rather than record a transition the machine would never have allowed.
/// </para>
/// </summary>
internal sealed record DeploymentLane
{
    private DeploymentLane(
        DeploymentEnvironmentId environment,
        DeploymentTransition deploymentTransition,
        DeploymentTransition verificationTransition,
        PromotionState deployedState,
        PromotionState verifiedState,
        string evidenceFileName,
        string verbLabel)
    {
        Environment = environment;
        DeploymentTransition = deploymentTransition;
        VerificationTransition = verificationTransition;
        DeployedState = deployedState;
        VerifiedState = verifiedState;
        EvidenceFileName = evidenceFileName;
        VerbLabel = verbLabel;

        // Cross-check this lane against the state machine rather than trusting the table above. A lane is a
        // convenience; the machine is the authority, and a lane that disagreed with it would be a second
        // source for facts that already have one.
        var predecessors = DeploymentStateMachine.PredecessorStatesFor(verificationTransition)
            ?? throw new InvalidOperationException($"{verificationTransition} has no declared predecessor states.");

        if (!predecessors.Contains(deployedState))
        {
            throw new InvalidOperationException(
                $"The {environment} lane says {verificationTransition} follows {deployedState}, but "
                + $"DeploymentStateMachine declares its predecessors as: {string.Join(", ", predecessors)}.");
        }

        if (DeploymentStateMachine.NextStateFor(deploymentTransition) != deployedState)
        {
            throw new InvalidOperationException(
                $"The {environment} lane says {deploymentTransition} lands in {deployedState}, but "
                + $"DeploymentStateMachine declares {DeploymentStateMachine.NextStateFor(deploymentTransition)}.");
        }

        if (DeploymentStateMachine.NextStateFor(verificationTransition) != verifiedState)
        {
            throw new InvalidOperationException(
                $"The {environment} lane says {verificationTransition} lands in {verifiedState}, but "
                + $"DeploymentStateMachine declares {DeploymentStateMachine.NextStateFor(verificationTransition)}.");
        }
    }

    /// <summary>The ratified environment this lane acts on.</summary>
    public DeploymentEnvironmentId Environment { get; }

    /// <summary>The transition that puts the certified bytes into this environment.</summary>
    public DeploymentTransition DeploymentTransition { get; }

    /// <summary>The transition that proves the bytes now running in this environment are the certified bytes.</summary>
    public DeploymentTransition VerificationTransition { get; }

    /// <summary>
    /// The state the bundle must already occupy before <see cref="DeploymentTransition"/> is legal — the
    /// caller's claim, which the ledger is the record of.
    ///
    /// <para>
    /// <b>Derived from the machine, not from this table.</b> It is the single declared predecessor of
    /// <see cref="DeploymentTransition"/>, so a lane cannot assert a predecessor the machine would reject:
    /// the constructor refuses a lane whose entry transition has more than one declared predecessor rather
    /// than silently taking the first.
    /// </para>
    /// </summary>
    public PromotionState DeploymentPredecessorState => DeploymentStateMachine.PredecessorStatesFor(DeploymentTransition) switch
    {
        [var only] => only,
        var predecessors => throw new InvalidOperationException(
            $"{DeploymentTransition} has {(predecessors?.Count ?? 0)} declared predecessors, so this lane cannot state one.")
    };

    /// <summary>The state the bundle occupies once <see cref="DeploymentTransition"/> has been recorded.</summary>
    public PromotionState DeployedState { get; }

    /// <summary>The state the bundle occupies once <see cref="VerificationTransition"/> has been recorded.</summary>
    public PromotionState VerifiedState { get; }

    /// <summary>
    /// Attempt-scoped verification evidence file name. Environment-specific so two lanes cannot address one
    /// file — the property whose absence destroyed two W9.4 envelopes
    /// (<c>HISTORICAL_EVIDENCE_OVERWRITE_DEFECT</c>).
    /// </summary>
    public string EvidenceFileName { get; }

    /// <summary>A short label for transcript and evidence file names, e.g. <c>DEV</c>.</summary>
    public string VerbLabel { get; }

    /// <summary>
    /// The name of the verb-level evidence document this lane writes (the human-facing record, distinct from
    /// the attempt-scoped envelope). Lane-qualified for the same reason the envelope is.
    /// </summary>
    public string DocumentPrefix => $"{VerbLabel}_";

    /// <summary>
    /// The <b>promotion precondition gate</b> applicable to this lane.
    ///
    /// <para>
    /// ENV-DEV is <i>deployed into</i>, not promoted into, so it has no promotion preconditions and this is
    /// <see langword="null"/>. ENV-TEST is promoted into, and the nine conditions the promotion directive
    /// names are evaluated before any transition is proposed. Modelled as a lane property rather than as a
    /// branch inside the verb so that the driver cannot forget to ask: the condition is the same expression
    /// that decides whether the gate runs at all.
    /// </para>
    /// </summary>
    public bool RequiresPromotionPreconditions => PromotedFrom is not null;

    /// <summary>
    /// The environment this lane's artifact must already be proven in, per rule R-4. <see langword="null"/>
    /// for the first environment, which is deployed into rather than promoted into.
    /// </summary>
    public DeploymentEnvironmentId? PromotedFrom => DeploymentEnvironment.For(Environment).PromotedFrom;

    /// <summary>
    /// The source environment whose recorded deployment and verification a promotion from this lane must
    /// name. <b>The same value as <see cref="PromotedFrom"/>, exposed under the name the promotion gate
    /// uses</b> — kept as one expression so the two cannot drift.
    /// </summary>
    public DeploymentEnvironmentId? PromotionSource => PromotedFrom;

    /// <summary>ENV-DEV: deployed into and verified there.</summary>
    public static DeploymentLane Dev { get; } = new(
        DeploymentEnvironmentId.DevEnv,
        DeploymentTransition.DeployToDev,
        DeploymentTransition.VerifyInDev,
        PromotionState.DeployedDev,
        PromotionState.VerifiedDev,
        "DEV_VERIFICATION.json",
        "DEV");

    /// <summary>ENV-TEST: promoted into from ENV-DEV, and verified there.</summary>
    public static DeploymentLane Test { get; } = new(
        DeploymentEnvironmentId.TestEnv,
        DeploymentTransition.PromoteToTest,
        DeploymentTransition.VerifyInTest,
        PromotionState.DeployedTest,
        PromotionState.VerifiedTest,
        "TEST_VERIFICATION.json",
        "TEST");

    /// <summary>
    /// The actor recorded in the lineage record's authorization for this lane.
    ///
    /// <para>
    /// <b>Lane-specific and stated once, because it is written into an immutable record.</b> ENV-DEV keeps
    /// the actor the W9.4 records already carry — <c>w9.4-env-dev-deployment-driver</c> — rather than being
    /// renamed to something symmetric, because the identity in a governed record is a fact about who acted
    /// and the W9.5 lane did not act in W9.4. A later reader comparing the DEV and TEST records must be able
    /// to see that two different drivers wrote them.
    /// </para>
    /// </summary>
    public string AuthorizationActor => Environment.Value == DeploymentEnvironmentId.Dev
        ? "w9.4-env-dev-deployment-driver"
        : "w9.5-env-test-promotion-driver";

    public override string ToString() => $"{Environment} via {DeploymentTransition}/{VerificationTransition}";
}
