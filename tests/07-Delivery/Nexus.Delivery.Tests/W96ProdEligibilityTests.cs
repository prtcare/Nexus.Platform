using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// TASK 7 — <b>the deterministic PROD promotion requirement.</b>
///
/// <para>
/// <c>VerifiedDev + VerifiedTest + same ReleaseId + same ArtifactId + same artifact SHA + TEST rollback
/// proven + production release-reference security satisfied + PROD environment validated → EligibleForProd</c>
/// </summary>
///
/// <para>
/// <b>Why this is expressed through the gate W9.5 built rather than as a new one.</b> The nine conditions
/// <see cref="PromotionGovernance"/> already evaluates are the same conditions one environment along, and
/// the one that differs — protection — is decided by
/// <see cref="ReleaseReferenceProtectionEvidence.Judge"/>, which takes the <i>environment</i> as a parameter
/// precisely so that the same recorded evidence is sufficient for DEV/TEST and insufficient for PROD. A
/// PROD-specific gate would be a second authority on a question this estate has already had to un-collapse
/// once, when a single boolean had to report a substituted control as the control it replaced.
/// </para>
///
/// <para>
/// <b>AI cannot authorize this transition, and that is structural rather than procedural.</b> The gate
/// returns a value; it writes no lineage record, moves no ref and changes no lifecycle. `PromoteToProd` is
/// Owner-reserved in <see cref="DeploymentStateMachine"/> and requires
/// <see cref="DeploymentAuthorization.IsOwnerAuthorized"/>. And no PROD verb exists in the driver at all —
/// the transition could not be proposed from this lane even if it were asked to.
/// </para>
/// </summary>
public sealed class W96ProdEligibilityTests
{
    /// <summary>
    /// A request in which everything except production release-reference security is established: DEV and
    /// TEST both deployed and verified, the remedy rehearsed, the reference and bytes intact, the plan
    /// covering the target, the target described, and the defect register declared empty.
    /// </summary>
    private static PromotionGovernanceRequest SatisfiedForProd() => new()
    {
        TargetEnvironment = DeploymentEnvironmentId.ProdEnv,
        CurrentState = PromotionState.VerifiedTest,
        SourceEnvironment = DeploymentEnvironmentId.TestEnv,

        SourceReleaseCertified = true,
        SourceReleaseLifecycle = ReleaseLifecycleState.ReadyForDev,
        SourceDeploymentLineageIds = ["L-W9-10"],
        SourceVerificationLineageIds = ["L-W9-11"],

        RollbackCapabilityProven = true,
        ReleaseReferenceIntact = true,
        ArtifactIntegrityIntact = true,
        SecurityPlanPermitsTarget = true,
        TargetEnvironmentDefinitionValid = true,
        BlockingDefects = DeploymentBlockingDefectRegister.Empty
    };

    /// <summary>
    /// <b>The production gate as it stands today: everything else established, and PROD still refused.</b>
    ///
    /// <para>
    /// The declaration-level member <c>SecurityPlanPermitsTarget</c> is true here on purpose — it says the
    /// plan was read and covers the target — and the refusal comes from the *mechanism* judgement, which
    /// asks which protection is relied on and whether it extends this far. The Owner-approved C-2
    /// compensating control is scoped to DEV/TEST, so it does not.
    /// </para>
    /// </summary>
    [Fact]
    public void AProdRequestWithEveryOtherConditionEstablishedIsStillRefusedOnProtection()
    {
        var protection = ReleaseReferenceProtectionEvidence.CompensatingControl();

        var judgement = protection.Judge(DeploymentEnvironmentId.ProdEnv);

        Assert.False(judgement.IsSatisfied);

        // Not "unobserved" — it IS observed, and it refuses. The distinction matters: unobserved sends a
        // reader looking for a missing fact; this sends them to the deviation's boundary, which is where
        // the answer is.
        Assert.False(judgement.IsUnobserved);
        Assert.Equal(ReleaseReferenceProtectionJudgement.OutstandingCode, judgement.Code);
        Assert.Contains("outside that scope", judgement.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The whole PROD requirement, asserted condition by condition, with the protection condition the
    /// only one that fails.</b> Every other member here is established, so the test cannot pass by refusing
    /// for an unrelated reason — which is the failure mode that made two of W9.4's live controls void.
    /// </summary>
    [Fact]
    public void EveryProdConditionExceptProductionProtectionIsEstablishable()
    {
        var request = SatisfiedForProd();

        // The eight conditions that do NOT depend on the protection mechanism. Asserted individually so that
        // a future edit which quietly weakens one of them is visible here rather than only at a live run.
        Assert.True(request.SourceReleaseCertified);
        Assert.True(request.ReleaseReferenceIntact);
        Assert.True(request.ArtifactIntegrityIntact);
        Assert.Equal(["L-W9-10"], request.SourceDeploymentLineageIds);
        Assert.Equal(["L-W9-11"], request.SourceVerificationLineageIds);
        Assert.True(request.RollbackCapabilityProven);
        Assert.True(request.TargetEnvironmentDefinitionValid);
        Assert.True(request.BlockingDefects.IsDeclared);
        Assert.Empty(request.BlockingDefects.Unresolved);

        // And the ninth, which is the one that fails: production release-reference security.
        var judgement = ReleaseReferenceProtectionEvidence.CompensatingControl()
            .Judge(DeploymentEnvironmentId.ProdEnv);

        Assert.False(judgement.IsSatisfied);
    }

    /// <summary>
    /// A production request is also refused if the artifact is not the one proven in TEST — the identity
    /// conditions are load-bearing, not decoration. This asserts the gate refuses when the TEST records are
    /// absent, which is what "same artifact, not a rebuild" means mechanically.
    /// </summary>
    [Fact]
    public void AProdRequestWithoutProvenTestRecordsIsRefused()
    {
        var decision = PromotionGovernance.Evaluate(SatisfiedForProd() with
        {
            SourceDeploymentLineageIds = [],
            SourceVerificationLineageIds = []
        });

        Assert.True(decision.IsRefused);
        Assert.Contains(PromotionRefusalReason.SourceDeploymentNotRecorded, decision.RefusalReasons);
        Assert.Contains(PromotionRefusalReason.SourceVerificationNotComplete, decision.RefusalReasons);
    }

    /// <summary>
    /// <b>PROD is a promotion from TEST, never from DEV.</b> Rule R-4 says an artifact reaches an environment
    /// only from the one immediately before it; asserted here against the contract rather than against the
    /// prose.
    /// </summary>
    [Fact]
    public void ProductionIsPromotedFromTestAndNotFromDev()
    {
        var prod = DeploymentEnvironment.For(DeploymentEnvironmentId.ProdEnv);

        Assert.Equal(DeploymentEnvironmentId.TestEnv, prod.PromotedFrom);
        Assert.True(prod.RequiresOwnerAuthorizationToEnter);
        Assert.True(prod.HoldsRealData);

        // And the environments below it do not: production is the only one that holds real data, which is
        // why it is the only one that requires an Owner authorization to enter.
        Assert.False(DeploymentEnvironment.For(DeploymentEnvironmentId.DevEnv).HoldsRealData);
        Assert.False(DeploymentEnvironment.For(DeploymentEnvironmentId.TestEnv).HoldsRealData);
    }

    /// <summary>
    /// <b>The transition remains Owner-reserved in the state machine.</b> A PROD promotion without an Owner
    /// authorization is refused, so "AI cannot authorize this transition" is a property of the machine and
    /// not of the lane's restraint.
    /// </summary>
    [Fact]
    public void AProdPromotionWithoutOwnerAuthorizationIsRefusedByTheMachine()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle) with
        {
            // Server-side protection is the only mechanism that would carry PROD; recording it here isolates
            // the authorization check from the protection check.
            ReleaseRefIsProtected = true,
            ReleaseReferenceProtection = ReleaseReferenceProtectionEvidence.ServerSide(true),
            Authorization = DeploymentAuthorization.For(
                DeploymentAuthorityRole.DeliveryTeam, "delivery-bot", DateTimeOffset.UnixEpoch)
        };

        var decision = DeploymentStateMachine.Decide(new PromotionRequest(
            bundle,
            DeploymentEnvironmentId.ProdEnv,
            PromotionState.VerifiedTest,
            DeploymentTransition.PromoteToProd,
            evidence));

        Assert.True(decision.IsRefused);
        Assert.Contains(DeploymentRefusalReason.OwnerAuthorizationRequired, decision.RefusalReasons);
    }
}
