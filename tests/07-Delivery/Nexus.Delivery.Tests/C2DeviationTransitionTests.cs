using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// The lifecycle half of the C-2 compensating control: that
/// <see cref="ReleaseLifecycleState.ReadyForDevPendingSecurityAction"/> can reach
/// <see cref="ReleaseLifecycleState.ReadyForDev"/> by <b>supplying evidence the gate accepts</b>, and by no
/// other means.
///
/// <para>
/// <b>Why this needed a contract change at all.</b> <see cref="ReleaseLifecycleMachine.PredecessorStatesFor"/>
/// listed only <see cref="ReleaseLifecycleState.ReleaseCertified"/> for
/// <see cref="ReleaseTransition.DeclareReadyForDev"/>, so the pending state had no edge out of it: a release
/// that correctly declared itself pending could never be un-pended, and the only way to reach DEV readiness
/// would have been to assign the state — which the deviation forbids outright. The edge was added; the
/// <i>gate</i> was not relaxed, and these tests are what keeps those two statements from being confused.
/// </para>
///
/// <para>
/// <b>The tests that matter here are the refusals.</b> Widening a predecessor is exactly the kind of change
/// that quietly becomes a shortcut, so the load-bearing assertions are that the pending state still refuses
/// to advance with an action outstanding, and still refuses on a technical deficiency — the second of which
/// proves the machine has not started deciding certification for itself.
/// </para>
/// </summary>
public sealed class C2DeviationTransitionTests
{
    private static ReleaseRecord Release
        => ReleaseTestData.Record();

    [Fact]
    public void DeclareReadyForDev_MayOriginateFromThePendingSecurityActionState()
    {
        var predecessors = ReleaseLifecycleMachine.PredecessorStatesFor(ReleaseTransition.DeclareReadyForDev);

        Assert.NotNull(predecessors);
        Assert.Contains(ReleaseLifecycleState.ReleaseCertified, predecessors!);
        Assert.Contains(ReleaseLifecycleState.ReadyForDevPendingSecurityAction, predecessors);

        // The transition still lands in exactly one state. If it could land in two, the choice between
        // "ready" and "ready pending" would be a branch that always produces something rather than a gate
        // that can refuse.
        Assert.Equal(ReleaseLifecycleState.ReadyForDev, ReleaseLifecycleMachine.NextStateFor(ReleaseTransition.DeclareReadyForDev));
    }

    /// <summary>
    /// <b>The deviation's evidence, supplied, advances the release.</b> This is the capability TASK C needs:
    /// the pending state is not a dead end, and the step out of it is a transition through the machine rather
    /// than an assignment.
    ///
    /// <para>
    /// The evidence is built as the deviation is written: both security actions observed — credential
    /// rotation confirmed by the Owner (C-1), and the release reference's protection established by the
    /// compensating control (C-2) in place of the server-side ruleset that cannot be installed on these
    /// repositories. Nothing here asserts that a ruleset exists; the mechanism is named as the substitute it
    /// is, and it is scoped to DEV/TEST.
    /// </para>
    /// </summary>
    [Fact]
    public void ReadyForDev_IsReachableFromThePendingState_WithTheDeviationEvidenceSupplied()
    {
        var decision = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReadyForDevPendingSecurityAction,
            ReleaseTransition.DeclareReadyForDev,
            Release,
            ReleaseTestData.FullyClearedEvidence());

        Assert.True(decision.IsAllowed);
        Assert.Equal(ReleaseLifecycleState.ReadyForDev, decision.ToState);
        Assert.Equal(ReleaseLifecycleState.ReadyForDevPendingSecurityAction, decision.FromState);
        Assert.Empty(decision.RefusalReasons);
    }

    /// <summary>
    /// <b>The widened edge is not a shortcut.</b> From the pending state, with an action still outstanding,
    /// unqualified DEV readiness is still refused — by the same gate that refused it from
    /// <c>ReleaseCertified</c>. If this ever passes, the predecessor change has become exactly the assignment
    /// the deviation forbids.
    ///
    /// <para>
    /// The outstanding action here is the compensating control's <b>verification</b>: the deviation is
    /// approved, and its rules have not been applied to this release's reference. That is the realistic
    /// outstanding state, and it is the one that must not be passable by approval alone.
    /// </para>
    /// </summary>
    [Fact]
    public void ReadyForDev_FromThePendingState_IsStillRefusedWhileAnActionIsOutstanding()
    {
        var decision = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReadyForDevPendingSecurityAction,
            ReleaseTransition.DeclareReadyForDev,
            Release,
            ReleaseTestData.ObservedEvidence(
                credentialRotationConfirmed: true,
                referenceProtection: ReleaseReferenceProtectionEvidence.CompensatingControl(verified: false)));

        Assert.False(decision.IsAllowed);
        Assert.True(decision.RefusedBecause(ReleaseRefusalReason.SecurityActionOutstanding));
    }

    /// <summary>
    /// The other half of the resume path, and the one that is easiest to get wrong: <b>an unrecorded
    /// protection mechanism refuses the resume too</b>, even with rotation confirmed. A release that named no
    /// mechanism has not established that its reference is protected, and "nobody said otherwise" is not
    /// evidence.
    /// </summary>
    [Fact]
    public void ReadyForDev_FromThePendingState_IsRefusedWhenNoProtectionMechanismIsRecorded()
    {
        var decision = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReadyForDevPendingSecurityAction,
            ReleaseTransition.DeclareReadyForDev,
            Release,
            ReleaseTestData.ObservedEvidence(
                credentialRotationConfirmed: true,
                referenceProtection: ReleaseReferenceProtectionEvidence.Unrecorded));

        Assert.False(decision.IsAllowed);
        Assert.True(decision.RefusedBecause(ReleaseRefusalReason.SecurityActionOutstanding));
    }

    /// <summary>
    /// And C-1's half on its own: rotation outstanding refuses the resume even when the reference's protection
    /// is fully established. Two actions, two independent conditions — evidence for one is not evidence for
    /// the other.
    /// </summary>
    [Fact]
    public void ReadyForDev_FromThePendingState_IsRefusedWhileRotationIsOutstanding()
    {
        var decision = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReadyForDevPendingSecurityAction,
            ReleaseTransition.DeclareReadyForDev,
            Release,
            ReleaseTestData.ObservedEvidence(
                credentialRotationConfirmed: false,
                referenceProtection: ReleaseTestData.CompensatingControl()));

        Assert.False(decision.IsAllowed);
        Assert.True(decision.RefusedBecause(ReleaseRefusalReason.SecurityActionOutstanding));
    }

    /// <summary>
    /// <b>And not a bypass of the certification judgement either.</b> With a technical deficiency present, the
    /// refusal from the pending state is the certification gate's own reason — carried through unchanged — so
    /// the machine and the gate cannot disagree about <i>why</i>.
    /// </summary>
    [Fact]
    public void ReadyForDev_FromThePendingState_CarriesTheGatesOwnTechnicalRefusal()
    {
        var evidence = new ReleaseGateEvidence
        {
            BuildIsCertified = true,
            SourceLineageComplete = true,
            ArtifactHashMatchesManifest = true,
            BundleIsComplete = true,
            ReleaseRefGoverned = true,
            DependencyManifestAvailable = true,
            ContractCompatibilityAcceptable = true,
            MigrationStateKnown = true,
            MigrationBackupEstablished = true,
            ConfigurationSchemaKnown = true,
            HealthDefinitionPresent = true,
            RollbackStateKnown = true,
            ConfigurationBoundaryClean = true,
            ReproducibilityEstablished = true,
            TestsPassed = true,
            CredentialRotationConfirmed = true,

            // The compensating control's evidence is supplied and both security actions are cleared. The one
            // deficiency is the secret scan, so the refusal below can only come from that.
            ReleaseReferenceProtection = ReleaseTestData.CompensatingControl(),
            SecretScanPassed = false
        };

        var decision = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReadyForDevPendingSecurityAction,
            ReleaseTransition.DeclareReadyForDev,
            Release,
            evidence);

        Assert.False(decision.IsAllowed);
        Assert.True(decision.RefusedBecause(ReleaseRefusalReason.SecretScanFindingsInActiveInput));

        // Not the outstanding-action refusal: this release fails on a technical condition, and reporting it as
        // a pending security action would tell the reader to wait for a human act that would not help.
        Assert.False(decision.RefusedBecause(ReleaseRefusalReason.SecurityActionOutstanding));
    }

    /// <summary>
    /// <b>The pending state cannot be re-declared from itself.</b> A release already waiting on an action that
    /// "declares itself waiting" again has moved nowhere, and an edge that permitted it would make the state
    /// self-sustaining — a shape that reads as progress in a log while nothing happens.
    /// </summary>
    [Fact]
    public void ThePendingState_CannotBeReDeclared_FromItself()
    {
        var decision = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReadyForDevPendingSecurityAction,
            ReleaseTransition.DeclareReadyForDevPendingSecurityAction,
            Release,
            ReleaseTestData.ObservedEvidence(credentialRotationConfirmed: false));

        Assert.False(decision.IsAllowed);
        Assert.True(decision.RefusedBecause(ReleaseRefusalReason.IllegalTransition));
    }

    /// <summary>
    /// <b>A drifted release has somewhere to be refused from.</b> A release waiting on a security action is
    /// precisely the release whose reference can drift while it waits, and
    /// <see cref="ReleaseRefusalReason.ReleaseReferenceDrift"/> is what the pre-deployment gate returns when
    /// it does. A state no refusal could be recorded from would leave that finding nowhere to go.
    /// </summary>
    [Fact]
    public void ADriftedRelease_CanBeRefused_FromThePendingState()
    {
        var decision = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReadyForDevPendingSecurityAction,
            ReleaseTransition.Refuse,
            Release,
            new ReleaseGateEvidence { Reason = "RELEASE_REFERENCE_DRIFT observed before ENV-DEV deployment." });

        Assert.True(decision.IsAllowed);
        Assert.Equal(ReleaseLifecycleState.Refused, decision.ToState);
    }

    /// <summary>
    /// The verdict that authorises the transition names the deviation rather than a ruleset, and the
    /// certification verdict distinguishes the pending case from the cleared one. Recorded as a test so the
    /// two verdict strings cannot silently converge.
    /// </summary>
    [Fact]
    public void TheCertificationVerdicts_StayDistinct()
    {
        var pending = ReleaseCertificationGate.Evaluate(
            Release,
            ReleaseTestData.ObservedEvidence(
                credentialRotationConfirmed: true,
                referenceProtection: ReleaseReferenceProtectionEvidence.CompensatingControl(verified: false)));

        var cleared = ReleaseCertificationGate.Evaluate(Release, ReleaseTestData.FullyClearedEvidence());

        Assert.Equal(ReleaseCertificationVerdict.CertifiedPendingSecurityAction, pending.Verdict);
        Assert.Equal(ReleaseCertificationVerdict.Certified, cleared.Verdict);

        // IsCertified means "the release itself is complete", and a pending release IS complete — every
        // technical condition is met. The strict member is IsDeployable, and that is the one the deviation
        // must not blur: a pending release is not deployable however complete it is.
        Assert.True(pending.IsCertified);
        Assert.False(pending.IsDeployable);
        Assert.True(cleared.IsDeployable);

        // The outstanding action is named, so a reader is told which human act is missing rather than that
        // something is wrong.
        Assert.Contains(pending.OutstandingSecurityActions, a => a.Contains("release", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <b>The scope of the deviation does not leak into the deployment stage.</b> The release the lifecycle
    /// machine just advanced to <c>ReadyForDev</c> is deployable in the environments the deviation covers and
    /// nowhere else — asserted through the deployment state machine, which is the stage that would actually
    /// move bytes.
    ///
    /// <para>
    /// This is the test the boolean could not have supported: while the control was recorded as
    /// <i>server-side protection verified</i>, the same evidence that carried ENV-DEV carried ENV-PROD, and
    /// nothing in the deployment gate could tell the difference.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDeviationCarriesATestPromotion_AndNotAProdPromotion()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle, DeploymentAuthorityRole.Owner);

        var toTest = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.PromoteToTest, PromotionState.VerifiedDev, evidence));

        Assert.True(toTest.IsAllowed);

        var toProd = DeploymentStateMachine.Decide(
            TestData.Request(
                bundle,
                DeploymentTransition.PromoteToProd,
                PromotionState.VerifiedTest,
                evidence,
                DeploymentEnvironmentId.ProdEnv));

        Assert.True(toProd.IsRefused);
        Assert.True(toProd.RefusedBecause(DeploymentRefusalReason.ReleaseRefNotProtected));

        // Not an evidence-completeness refusal: the deviation's evidence is complete and the answer is still
        // no. Collapsing the two would send someone to supply evidence that cannot change the answer.
        Assert.False(toProd.RefusedBecause(DeploymentRefusalReason.EvidenceIncomplete));
    }
}
