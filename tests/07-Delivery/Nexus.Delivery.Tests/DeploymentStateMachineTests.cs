using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// The state machine's gates, one assertion per gate.
///
/// <para>
/// The shape of these tests follows the estate's own rule about safety checks: each negative assertion
/// names the typed refusal it expects, so it cannot pass because the machine refused for some other
/// reason. <see cref="Machine_IsNotVacuouslyRefusing"/> states the discriminator explicitly — the same
/// evidence that is refused for one broken member is allowed when that member is intact, so a machine
/// that simply refused everything would fail that test rather than pass all the others.
/// </para>
/// </summary>
public sealed class DeploymentStateMachineTests
{
    // ---------------------------------------------------------------------------------------------
    // Totality — the property that makes a new transition a visible act rather than a silent default.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EveryTransitionHasADeclaredNextState()
    {
        var missing = Enum.GetValues<DeploymentTransition>()
            .Where(t => DeploymentStateMachine.NextStateFor(t) is null)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These transitions have no gate and would fall through to an unknown state: " + string.Join(", ", missing));
    }

    // ---------------------------------------------------------------------------------------------
    // Build
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Build_IsRefusedWhenTheWorkingTreeIsDirty()
    {
        var decision = DeploymentStateMachine.ValidateBuild(
            TestData.Bundle(),
            new DeploymentGateEvidence { WorkingTreeIsDirty = true });

        Assert.True(decision.IsRefused);
        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.SourceWorkingTreeIsDirty));
    }

    [Fact]
    public void Build_IsRefusedWhenTheTreeStateWasNeverObserved()
    {
        // Absence of evidence is a refusal, never an assumption of cleanliness.
        var decision = DeploymentStateMachine.ValidateBuild(TestData.Bundle(), DeploymentGateEvidence.Empty);

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.EvidenceIncomplete));
    }

    [Fact]
    public void Build_IsAllowed_WhenTheTreeIsClean()
    {
        var decision = DeploymentStateMachine.ValidateBuild(
            TestData.Bundle(),
            new DeploymentGateEvidence { WorkingTreeIsDirty = false });

        Assert.True(decision.IsAllowed);
        Assert.Equal(PromotionState.Built, decision.NextState);
    }

    /// <summary>
    /// The hole this test exists for: <c>Build</c>'s predecessor list is empty ("from anywhere"), so a
    /// table-driven evaluation would let it through without ever consulting the dirty-tree gate. The
    /// entry point must route it to <c>ValidateBuild</c> instead.
    /// </summary>
    [Fact]
    public void Decide_RoutesBuildThroughItsGate_RatherThanTheFromAnywhereBranch()
    {
        var decision = DeploymentStateMachine.Decide(new PromotionRequest(
            TestData.Bundle(),
            DeploymentEnvironmentId.DevEnv,
            PromotionState.Built,
            DeploymentTransition.Build,
            new DeploymentGateEvidence { WorkingTreeIsDirty = true }));

        Assert.True(decision.IsRefused);
        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.SourceWorkingTreeIsDirty));
    }

    // ---------------------------------------------------------------------------------------------
    // Register
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Register_IsAllowedFromBuilt()
    {
        var bundle = TestData.Bundle();
        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.Register, PromotionState.Built));

        Assert.True(decision.IsAllowed);
        Assert.Equal(PromotionState.Registered, decision.NextState);
    }

    [Fact]
    public void Register_IsRefusedWhenTheBundleIdIsAlreadyRegistered()
    {
        var bundle = TestData.Bundle();
        var request = TestData.Request(bundle, DeploymentTransition.Register, PromotionState.Built)
            with { BundleIdAlreadyRegistered = true };

        var decision = DeploymentStateMachine.Decide(request);

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.RegistryRefused));
    }

    [Fact]
    public void Register_IsRefusedWhenTheBundleWouldDeployAnExcludedUnit()
    {
        var bundle = TestData.Bundle();
        var request = TestData.Request(bundle, DeploymentTransition.Register, PromotionState.Built)
            with { ExcludedUnitsInBundle = [TestData.ApiUnit] };

        var decision = DeploymentStateMachine.Decide(request);

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.UnitExcluded));
    }

    // ---------------------------------------------------------------------------------------------
    // Legality
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(PromotionState.Live, DeploymentTransition.Register)]
    [InlineData(PromotionState.Built, DeploymentTransition.VerifyInDev)]
    [InlineData(PromotionState.Registered, DeploymentTransition.PromoteToProd)]
    [InlineData(PromotionState.VerifiedDev, DeploymentTransition.PromoteToProd)]
    public void ATransitionFromTheWrongState_IsRefusedAsIllegal(PromotionState from, DeploymentTransition transition)
    {
        var decision = DeploymentStateMachine.Decide(TestData.Request(TestData.Bundle(), transition, from));

        Assert.True(decision.IsRefused);
        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.IllegalTransition));
    }

    // ---------------------------------------------------------------------------------------------
    // Verification
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void VerifyInDev_IsAllowedOnFullyObservedHealthyEvidence()
    {
        var bundle = TestData.Bundle();
        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.VerifyInDev, PromotionState.DeployedDev));

        Assert.True(decision.IsAllowed);
        Assert.Equal(PromotionState.VerifiedDev, decision.NextState);
    }

    [Fact]
    public void VerifyInDev_IsRefusedWhenNothingObservedReadiness()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle) with { ReadinessObserved = null };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.VerifyInDev, PromotionState.DeployedDev, evidence));

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.EvidenceIncomplete));
    }

    [Fact]
    public void VerifyInDev_IsRefusedWhenReadinessWasObservedFalse()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle) with { ReadinessObserved = false };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.VerifyInDev, PromotionState.DeployedDev, evidence));

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.ReadinessNotObserved));
    }

    [Fact]
    public void VerifyInDev_IsRefusedWhenTheTargetHoldsDifferentBytes()
    {
        var bundle = TestData.Bundle();
        var observed = bundle.AllArtifacts.ToDictionary(a => a.UnitId, _ => TestData.Digest('f'));
        var evidence = TestData.HealthyEvidence(bundle) with { ObservedDigests = observed };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.VerifyInDev, PromotionState.DeployedDev, evidence));

        // This is assertion A-3 of the first proof: the digest in the target must equal the manifest's.
        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.DigestMismatchInTarget));
    }

    [Fact]
    public void VerifyInDev_IsRefusedWhenTheTargetDatabaseIsAhead()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle) with
        {
            MigrationCompatibility = MigrationCompatibility.Ahead
        };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.VerifyInDev, PromotionState.DeployedDev, evidence));

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.MigrationSetAhead));
    }

    [Fact]
    public void VerifyInDev_IsRefusedWhenTheTargetSchemaDiverges()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle) with
        {
            MigrationCompatibility = MigrationCompatibility.Divergent
        };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.VerifyInDev, PromotionState.DeployedDev, evidence));

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.MigrationSetDivergent));
    }

    // ---------------------------------------------------------------------------------------------
    // Promotion
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void PromoteToTest_IsRefusedWhenTheConfigurationSchemaIsNotSatisfied()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle) with { ConfigurationSchemaSatisfied = false };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.PromoteToTest, PromotionState.VerifiedDev, evidence));

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.ConfigurationSchemaUnsatisfied));
    }

    [Fact]
    public void PromoteToTest_IsRefusedWhenTheReleaseRefIsNotProtected()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle) with { ReleaseRefIsProtected = false };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.PromoteToTest, PromotionState.VerifiedDev, evidence));

        // W8-DEBT-01 escalated: if the ref a release is built from is not protected, the build-once
        // guarantee has no governed source and the promotion is void.
        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.ReleaseRefNotProtected));
    }

    [Fact]
    public void PromoteToProd_IsRefusedWithoutAnOwnerAuthorization()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle, DeploymentAuthorityRole.DeliveryTeam) with
        {
            ReleaseReferenceProtection = ReleaseReferenceProtectionEvidence.ServerSide()
        };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(
                bundle,
                DeploymentTransition.PromoteToProd,
                PromotionState.VerifiedTest,
                evidence,
                DeploymentEnvironmentId.ProdEnv));

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.OwnerAuthorizationRequired));
    }

    /// <summary>
    /// The production path, with the only mechanism that can carry it: a remote ruleset confirmed by an
    /// authorized check. <b>The C-2 compensating control cannot stand in for it here</b> — that is the whole
    /// point of its DEV/TEST scope, and
    /// <see cref="C2DeviationTransitionTests.TheDeviationCarriesATestPromotion_AndNotAProdPromotion"/> is the
    /// other half of this pair.
    /// </summary>
    [Fact]
    public void PromoteToProd_IsAllowedWithAnOwnerAuthorizationAndServerSideProtection()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle, DeploymentAuthorityRole.Owner) with
        {
            ReleaseReferenceProtection = ReleaseReferenceProtectionEvidence.ServerSide()
        };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(
                bundle,
                DeploymentTransition.PromoteToProd,
                PromotionState.VerifiedTest,
                evidence,
                DeploymentEnvironmentId.ProdEnv));

        Assert.True(decision.IsAllowed);
        Assert.Equal(PromotionState.DeployedProd, decision.NextState);
    }

    /// <summary>
    /// <b>The deviation's boundary, as the deployment stage enforces it.</b> The same evidence that promotes
    /// to ENV-TEST is refused for ENV-PROD — refused as a decision, not as missing evidence, because a reader
    /// told "evidence incomplete" would go and collect evidence that cannot change the answer.
    /// </summary>
    [Fact]
    public void PromoteToProd_IsRefusedWhileTheCompensatingControlIsTheOnlyProtection()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle, DeploymentAuthorityRole.Owner);

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(
                bundle,
                DeploymentTransition.PromoteToProd,
                PromotionState.VerifiedTest,
                evidence,
                DeploymentEnvironmentId.ProdEnv));

        Assert.True(decision.IsRefused);
        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.ReleaseRefNotProtected));
        Assert.False(decision.RefusedBecause(DeploymentRefusalReason.EvidenceIncomplete));
    }

    /// <summary>
    /// A production promotion that declared a non-production target is refused as well. The declared target is
    /// what the lineage record writes down, so reading the declaration alone would let a mislabelled promotion
    /// reach production under a DEV/TEST-only deviation; the check consults the transition too, for exactly
    /// that case.
    /// </summary>
    [Fact]
    public void PromoteToProd_IsRefusedWhenItMislabelsItsTargetAsATestEnvironment()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle, DeploymentAuthorityRole.Owner);

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(
                bundle,
                DeploymentTransition.PromoteToProd,
                PromotionState.VerifiedTest,
                evidence,
                DeploymentEnvironmentId.TestEnv));

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.ReleaseRefNotProtected));
    }

    [Fact]
    public void PromoteToProd_IsStillRefusedWhenAnOwnerAuthorizesADigestMismatch()
    {
        // Authorization is not a bypass. An owner who authorizes a broken promotion gets a refusal,
        // because the digest gate is a fact about the bytes and no authority changes facts.
        var bundle = TestData.Bundle();
        var observed = bundle.AllArtifacts.ToDictionary(a => a.UnitId, _ => TestData.Digest('f'));
        var evidence = TestData.HealthyEvidence(bundle, DeploymentAuthorityRole.Owner) with
        {
            ObservedDigests = observed,
            ReleaseReferenceProtection = ReleaseReferenceProtectionEvidence.ServerSide()
        };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(
                bundle,
                DeploymentTransition.PromoteToProd,
                PromotionState.VerifiedTest,
                evidence,
                DeploymentEnvironmentId.ProdEnv));

        Assert.True(decision.IsRefused);
        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.DigestMismatchInTarget));
    }

    /// <summary>
    /// And a promotion whose reference protection was never recorded at all is refused as
    /// <c>EvidenceIncomplete</c> rather than as a failure — the estate's NOT_RUN rule, applied one stage below
    /// the release gate that makes the same distinction.
    /// </summary>
    [Fact]
    public void PromoteToTest_IsRefusedWhenNoProtectionMechanismIsRecorded()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle) with
        {
            ReleaseReferenceProtection = ReleaseReferenceProtectionEvidence.Unrecorded
        };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.PromoteToTest, PromotionState.VerifiedDev, evidence));

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.EvidenceIncomplete));
        Assert.False(decision.RefusedBecause(DeploymentRefusalReason.ReleaseRefNotProtected));
    }

    // ---------------------------------------------------------------------------------------------
    // Quarantine
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Quarantine_IsAllowedFromAnyState_WithAReasonAndAnActor()
    {
        foreach (var from in new[] { PromotionState.Built, PromotionState.VerifiedDev, PromotionState.Live, PromotionState.Failed })
        {
            var decision = DeploymentStateMachine.Decide(
                TestData.Request(TestData.Bundle(), DeploymentTransition.Quarantine, from));

            Assert.True(decision.IsAllowed, $"Quarantine should be reachable from {from}.");
            Assert.Equal(PromotionState.Quarantined, decision.NextState);
        }
    }

    [Fact]
    public void Quarantine_IsRefusedWithoutAReason()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle) with { Reason = null };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.Quarantine, PromotionState.Live, evidence));

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.EvidenceIncomplete));
    }

    [Fact]
    public void AQuarantinedBundle_RefusesEveryTransitionExceptTheOwnerClear()
    {
        var bundle = TestData.Bundle();

        foreach (var transition in Enum.GetValues<DeploymentTransition>().Where(t => t != DeploymentTransition.ClearQuarantine))
        {
            var decision = DeploymentStateMachine.Decide(
                TestData.Request(bundle, transition, PromotionState.Quarantined));

            Assert.True(
                decision.RefusedBecause(DeploymentRefusalReason.BundleQuarantined),
                $"A quarantined bundle must refuse {transition} with BundleQuarantined, not '{decision}'.");
        }
    }

    [Fact]
    public void ClearQuarantine_IsRefusedWithoutAnOwnerAuthorization()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle, DeploymentAuthorityRole.OnCall);

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.ClearQuarantine, PromotionState.Quarantined, evidence));

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.OwnerAuthorizationRequired));
    }

    [Fact]
    public void ClearQuarantine_ReturnsTheBundleToRegistered_NotToItsPriorState()
    {
        // A cleared bundle must re-verify in every environment it had reached. Returning it to its
        // prior state would let a quarantine be cleared and the bundle promoted onward without
        // re-observation — the one outcome a quarantine exists to prevent.
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle, DeploymentAuthorityRole.Owner);

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.ClearQuarantine, PromotionState.Quarantined, evidence));

        Assert.True(decision.IsAllowed);
        Assert.Equal(PromotionState.Registered, decision.NextState);
    }

    // ---------------------------------------------------------------------------------------------
    // Rollback
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Rollback_IsRefusedWhenThePreviousBundleIsUnavailable()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle) with { PreviousBundleAvailable = false };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.Rollback, PromotionState.Live, evidence));

        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.PreviousBundleUnavailable));
    }

    [Fact]
    public void Rollback_IsAllowedWhenThePreviousBundleIsRetainedAndThePathIsRehearsed()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle) with
        {
            PreviousBundle = TestData.Bundle("nexus-2026.09.15-2b91e0"),
            PreviousBundleAvailable = true,
            RollbackRehearsed = true,
            Reason = "Rolling back after a regression in ENV-TEST."
        };

        var decision = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.Rollback, PromotionState.Live, evidence));

        Assert.True(decision.IsAllowed);
        Assert.Equal(PromotionState.RolledBack, decision.NextState);
    }

    [Fact]
    public void Rollback_AcrossAMigrationBoundary_RequiresAnOwnerAndARehearsal()
    {
        var bundle = TestData.Bundle();
        var evidence = TestData.HealthyEvidence(bundle) with
        {
            PreviousBundle = TestData.Bundle("nexus-2026.09.15-2b91e0"),
            PreviousBundleAvailable = true,
            RollbackRehearsed = false,
            MigrationCompatibility = MigrationCompatibility.Divergent,
            Authorization = DeploymentAuthorization.For(DeploymentAuthorityRole.DeliveryTeam, "delivery-bot", DateTimeOffset.UnixEpoch),
            Reason = "Cross-boundary rollback attempt."
        };

        var plan = RollbackPlan.Plan(new RollbackRequest(
            bundle.BundleId,
            TestData.Bundle("nexus-2026.09.15-2b91e0").BundleId,
            DeploymentEnvironmentId.ProdEnv,
            PreviousBundleAvailable: true,
            CrossesMigrationBoundary: true,
            Rehearsed: false,
            Authorization: evidence.Authorization,
            Reason: evidence.Reason));

        Assert.True(plan.IsPermitted == false);
        Assert.Contains(DeploymentRefusalReason.OwnerAuthorizationRequired, plan.RefusalReasons);
        Assert.Contains(DeploymentRefusalReason.EvidenceIncomplete, plan.RefusalReasons);
    }

    // ---------------------------------------------------------------------------------------------
    // Non-vacuity
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The discriminator. Same bundle, same evidence, same transition — one broken member flips the
    /// decision. A machine that refused everything, or allowed everything, fails here rather than passing
    /// every gate test above by being uniformly wrong.
    /// </summary>
    [Fact]
    public void Machine_IsNotVacuouslyRefusing()
    {
        var bundle = TestData.Bundle();

        var baseline = DeploymentStateMachine.Decide(
            TestData.Request(bundle, DeploymentTransition.VerifyInDev, PromotionState.DeployedDev));

        var broken = DeploymentStateMachine.Decide(
            TestData.Request(
                bundle,
                DeploymentTransition.VerifyInDev,
                PromotionState.DeployedDev,
                TestData.HealthyEvidence(bundle) with { ReadinessObserved = false }));

        Assert.True(baseline.IsAllowed);
        Assert.True(broken.IsRefused);
    }

    [Fact]
    public void ARefusalAlwaysNamesAtLeastOneReason()
    {
        var decision = DeploymentStateMachine.Decide(
            TestData.Request(TestData.Bundle(), DeploymentTransition.Register, PromotionState.Live));

        Assert.True(decision.IsRefused);
        Assert.NotEmpty(decision.RefusalReasons);
        Assert.DoesNotContain(DeploymentRefusalReason.None, decision.RefusalReasons);
    }
}
