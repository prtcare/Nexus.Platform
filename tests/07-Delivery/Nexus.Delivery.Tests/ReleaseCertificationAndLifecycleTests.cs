using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// Release certification and the lifecycle machine: that DEV readiness is established rather than declared,
/// and that a release which is complete but waiting on a security action lands in its own typed state
/// instead of being refused or waved through.
/// </summary>
public sealed class ReleaseCertificationTests
{
    [Fact]
    public void Evaluate_CertifiesACompleteRelease_WhenNothingIsOutstanding()
    {
        var release = ReleaseTestData.Record();

        var decision = ReleaseCertificationGate.Evaluate(release, ReleaseTestData.FullyClearedEvidence());

        Assert.Equal(ReleaseCertificationVerdict.Certified, decision.Verdict);
        Assert.True(decision.IsDeployable);
        Assert.Equal(release.ReleaseId, decision.ReleaseId);
        Assert.Equal("RELEASE_CERTIFIED", ReleaseCertificationGate.CertifiedVerdict);
    }

    /// <summary>
    /// TASK 6's requirement, and the reason the verdict is three-valued. Every technical condition is met;
    /// the credential rotation is outstanding. Refusing would send someone to look for a defect that is not
    /// there, and certifying plainly would produce a release that looks deployable and is not.
    /// </summary>
    [Fact]
    public void Evaluate_CertifiesWithAPendingSecurityAction_WhenRotationIsOutstanding()
    {
        var release = ReleaseTestData.Record();

        var decision = ReleaseCertificationGate.Evaluate(
            release,
            ReleaseTestData.ObservedEvidence(
                credentialRotationConfirmed: false,
                referenceProtection: ReleaseTestData.CompensatingControl()));

        Assert.Equal(ReleaseCertificationVerdict.CertifiedPendingSecurityAction, decision.Verdict);
        Assert.False(decision.IsDeployable);
        Assert.True(decision.IsCertified);
        Assert.Empty(decision.RefusalReasons);
        Assert.Single(decision.OutstandingSecurityActions);
        Assert.Contains("CREDENTIAL_ROTATION_OUTSTANDING", decision.OutstandingSecurityActions[0], StringComparison.Ordinal);
        Assert.Equal("READY_FOR_DEV_PENDING_SECURITY_ACTION", ReleaseCertificationGate.PendingSecurityActionVerdict);
    }

    [Fact]
    public void Evaluate_NamesBothSecurityActionsWhenBothAreOutstanding()
    {
        var decision = ReleaseCertificationGate.Evaluate(
            ReleaseTestData.Record(),
            ReleaseTestData.ObservedEvidence(
                credentialRotationConfirmed: false,
                referenceProtection: ReleaseReferenceProtectionEvidence.CompensatingControl(verified: false)));

        Assert.Equal(2, decision.OutstandingSecurityActions.Count);
        Assert.Equal(ReleaseCertificationVerdict.CertifiedPendingSecurityAction, decision.Verdict);
        Assert.Contains(decision.OutstandingSecurityActions, a => a.StartsWith("CREDENTIAL_ROTATION_OUTSTANDING", StringComparison.Ordinal));
        Assert.Contains(decision.OutstandingSecurityActions, a => a.StartsWith(ReleaseReferenceProtectionJudgement.OutstandingCode, StringComparison.Ordinal));

        // The outstanding line names the SUBSTITUTE, not the control it stands in for. A reader who saw
        // "server-side protection outstanding" would go looking for a ruleset to install, which is not what
        // this release is waiting on and is not something these repositories can do.
        Assert.Contains(decision.OutstandingSecurityActions, a => a.Contains("C-2 compensating control", StringComparison.Ordinal));
        Assert.DoesNotContain(decision.OutstandingSecurityActions, a => a.Contains("server-side", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>An unobserved security action is reported as unconfirmed, not assumed either way.</summary>
    [Fact]
    public void Evaluate_ReportsAnUnobservedRotationAsUnconfirmed()
    {
        var decision = ReleaseCertificationGate.Evaluate(
            ReleaseTestData.Record(),
            ReleaseTestData.ObservedEvidence(
                credentialRotationConfirmed: null,
                referenceProtection: ReleaseReferenceProtectionEvidence.Unrecorded));

        Assert.Equal(ReleaseCertificationVerdict.CertifiedPendingSecurityAction, decision.Verdict);
        Assert.Contains(decision.OutstandingSecurityActions, a => a.StartsWith("CREDENTIAL_ROTATION_UNCONFIRMED", StringComparison.Ordinal));
        Assert.Contains(decision.OutstandingSecurityActions, a => a.StartsWith(ReleaseReferenceProtectionJudgement.UnrecordedCode, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("artifactHash")]
    [InlineData("migration")]
    [InlineData("health")]
    [InlineData("releaseRef")]
    [InlineData("secretScan")]
    [InlineData("tests")]
    [InlineData("rollback")]
    public void Evaluate_RefusesWhenOneRequiredConditionFails(string deficiency)
    {
        var release = ReleaseTestData.Record();
        var evidence = ReleaseTestData.FullyClearedEvidence();

        var broken = deficiency switch
        {
            "artifactHash" => evidence with { ArtifactHashMatchesManifest = false },
            "migration" => evidence with { MigrationStateKnown = false },
            "health" => evidence with { HealthDefinitionPresent = false },
            "releaseRef" => evidence with { ReleaseRefGoverned = false },
            "secretScan" => evidence with { SecretScanPassed = false },
            "tests" => evidence with { TestsPassed = false },
            "rollback" => evidence with { RollbackStateKnown = false },
            _ => throw new ArgumentOutOfRangeException(nameof(deficiency))
        };

        var decision = ReleaseCertificationGate.Evaluate(release, broken);

        Assert.Equal(ReleaseCertificationVerdict.Refused, decision.Verdict);
        Assert.NotEmpty(decision.RefusalReasons);
    }

    /// <summary>
    /// Null is "not observed", never "fine". The estate's rule: a thing that could not be checked is
    /// reported as not-run and never as a pass.
    /// </summary>
    [Fact]
    public void Evaluate_RefusesAnUnobservedCondition_WithEvidenceIncompleteRatherThanItsTypedReason()
    {
        var evidence = ReleaseTestData.FullyClearedEvidence();
        var unobserved = evidence with { MigrationStateKnown = null };

        var decision = ReleaseCertificationGate.Evaluate(ReleaseTestData.Record(), unobserved);

        Assert.Equal(ReleaseCertificationVerdict.Refused, decision.Verdict);
        Assert.True(decision.RefusedBecause(ReleaseRefusalReason.EvidenceIncomplete));
        Assert.False(decision.RefusedBecause(ReleaseRefusalReason.MigrationStateUnknown));
    }

    /// <summary>The bundle's completeness is judged from the record, so a caller cannot assert it away.</summary>
    [Fact]
    public void Evaluate_JudgesBundleCompletenessFromTheRecord_NotFromEvidence()
    {
        var release = ReleaseTestData.Record(contracts: []);

        // The caller claims the bundle is complete. The record disagrees, and the record wins.
        var decision = ReleaseCertificationGate.Evaluate(release, ReleaseTestData.FullyClearedEvidence());

        Assert.Equal(ReleaseCertificationVerdict.Refused, decision.Verdict);
        Assert.True(decision.RefusedBecause(ReleaseRefusalReason.ReleaseBundleIncomplete));
        Assert.Contains(ReleaseCertificationGate.MissingBundleElements(release), m => m == "contractVersions");
    }

    /// <summary>
    /// The configuration boundary: a value can never be recorded where a name belongs, and the refusal
    /// happens at the type rather than at the gate.
    ///
    /// <para>
    /// That placement is the control. A non-name-shaped configuration key is refused by
    /// <see cref="ReleaseRecord"/>'s own constructor, so such a record cannot be constructed at all and the
    /// gate never has to catch it — which is why the gate does <b>not</b> re-check keys. Re-checking would
    /// be a guard that cannot fail, and this estate has already recorded that a guard which cannot fail is
    /// worse than no guard because it reads as protection while establishing nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void ConfigurationBoundary_RefusesAValueWhereANameBelongs_AtConstruction()
    {
        var release = ReleaseTestData.Record();

        Assert.True(ReleaseCertificationGate.ConfigurationBoundaryIsClean(release, out var clean));
        Assert.Contains("No environment endpoint", clean, StringComparison.Ordinal);

        var generated = TestData.TokenValue();

        // The record cannot be built at all: the credential-shaped key is refused before any gate runs.
        var error = Assert.Throws<ArgumentException>(() => ReleaseTestData.Record(configurationKeys: [generated]));

        Assert.Contains("Invariant L-1 forbids a value", error.Message, StringComparison.Ordinal);

        // And the value itself is never reproduced in the refusal.
        Assert.DoesNotContain(generated, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one member the boundary DOES have to check: a contract version is not name-constrained by its own
    /// type, so it is the only place a value could be smuggled through.
    /// </summary>
    [Fact]
    public void ConfigurationBoundary_RefusesAContractVersionThatIsNotAVersion()
    {
        var carrying = ReleaseTestData.Record(
            contracts: [new ContractVersion("Nexus.ProductCore.Contracts", TestData.TokenValue())]);

        Assert.False(ReleaseCertificationGate.ConfigurationBoundaryIsClean(carrying, out var detail));
        Assert.Contains("not a version", detail, StringComparison.Ordinal);
    }
}

/// <summary>The release lifecycle machine's gates and its two distinct readiness states.</summary>
public sealed class ReleaseLifecycleMachineTests
{
    private static ReleaseRecord Release => ReleaseTestData.Record();

    [Fact]
    public void EveryTransitionHasADeclaredNextState()
    {
        var missing = Enum.GetValues<ReleaseTransition>()
            .Where(t => ReleaseLifecycleMachine.NextStateFor(t) is null)
            .ToList();

        Assert.True(missing.Count == 0, "Transitions with no declared destination: " + string.Join(", ", missing));
    }

    [Fact]
    public void CertifyBuild_IsAFactory_AndRefusesAnUncertifiedBuild()
    {
        var allowed = ReleaseLifecycleMachine.CertifyBuild(
            new ReleaseGateEvidence { BuildIsCertified = true, SourceLineageComplete = true });

        Assert.True(allowed.IsAllowed);
        Assert.Equal(ReleaseLifecycleState.BuildCertified, allowed.ToState);

        var refused = ReleaseLifecycleMachine.CertifyBuild(
            new ReleaseGateEvidence { BuildIsCertified = false, SourceLineageComplete = true });

        Assert.False(refused.IsAllowed);
        Assert.Contains(ReleaseRefusalReason.BuildNotCertified, refused.RefusalReasons);
    }

    [Fact]
    public void Decide_RefusesAnIllegalTransition()
    {
        var decision = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.BuildCertified,
            ReleaseTransition.DeclareReadyForDev,
            Release,
            ReleaseTestData.FullyClearedEvidence());

        Assert.False(decision.IsAllowed);
        Assert.True(decision.RefusedBecause(ReleaseRefusalReason.IllegalTransition));
    }

    [Fact]
    public void DeclareReadyForDev_IsRefusedWhileASecurityActionIsOutstanding()
    {
        var decision = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReleaseCertified,
            ReleaseTransition.DeclareReadyForDev,
            Release,
            ReleaseTestData.ObservedEvidence(credentialRotationConfirmed: false));

        // The certification gate does not refuse this release — it is complete. The readiness gate does,
        // because unqualified readiness is a claim this release cannot support.
        Assert.False(decision.IsAllowed);
        Assert.True(decision.RefusedBecause(ReleaseRefusalReason.SecurityActionOutstanding));
    }

    [Fact]
    public void DeclareReadyForDev_IsAllowedWhenNothingIsOutstanding()
    {
        var decision = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReleaseCertified,
            ReleaseTransition.DeclareReadyForDev,
            Release,
            ReleaseTestData.FullyClearedEvidence());

        Assert.True(decision.IsAllowed);
        Assert.Equal(ReleaseLifecycleState.ReadyForDev, decision.ToState);
    }

    [Fact]
    public void DeclareReadyForDevPendingSecurityAction_IsAllowedOnlyWhenSomethingIsOutstanding()
    {
        var pending = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReleaseCertified,
            ReleaseTransition.DeclareReadyForDevPendingSecurityAction,
            Release,
            ReleaseTestData.ObservedEvidence(credentialRotationConfirmed: false));

        Assert.True(pending.IsAllowed);
        Assert.Equal(ReleaseLifecycleState.ReadyForDevPendingSecurityAction, pending.ToState);

        // With nothing outstanding, the pending state must not be reachable — otherwise it is a way around
        // the unqualified readiness gate rather than a description of a real situation.
        var shortcut = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReleaseCertified,
            ReleaseTransition.DeclareReadyForDevPendingSecurityAction,
            Release,
            ReleaseTestData.FullyClearedEvidence());

        Assert.False(shortcut.IsAllowed);
        Assert.True(shortcut.RefusedBecause(ReleaseRefusalReason.NoOutstandingSecurityAction));
    }

    [Fact]
    public void CertifyRelease_DelegatesToTheCertificationGate_SoTheTwoCannotDisagree()
    {
        var release = ReleaseTestData.Record(contracts: []);
        var evidence = ReleaseTestData.FullyClearedEvidence();

        var gate = ReleaseCertificationGate.Evaluate(release, evidence);
        var machine = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReleaseDraft,
            ReleaseTransition.CertifyRelease,
            release,
            evidence);

        Assert.Equal(ReleaseCertificationVerdict.Refused, gate.Verdict);
        Assert.False(machine.IsAllowed);
        Assert.Equal(
            gate.RefusalReasons.OrderBy(r => r).ToList(),
            machine.RefusalReasons.OrderBy(r => r).ToList());
    }

    [Fact]
    public void Withdraw_IsOwnerReserved_AndTerminal()
    {
        var unauthorized = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReleaseCertified,
            ReleaseTransition.Withdraw,
            Release,
            new ReleaseGateEvidence { Reason = "No longer wanted." });

        Assert.False(unauthorized.IsAllowed);
        Assert.True(unauthorized.RefusedBecause(ReleaseRefusalReason.OwnerAuthorizationRequired));

        var authorized = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReleaseCertified,
            ReleaseTransition.Withdraw,
            Release,
            new ReleaseGateEvidence
            {
                Reason = "Owner withdrew it.",
                Authorization = DeploymentAuthorization.For(DeploymentAuthorityRole.Owner, "owner", DateTimeOffset.UnixEpoch)
            });

        Assert.True(authorized.IsAllowed);
        Assert.Equal(ReleaseLifecycleState.Withdrawn, authorized.ToState);

        var afterWithdrawal = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.Withdrawn,
            ReleaseTransition.DeclareReadyForDev,
            Release,
            ReleaseTestData.FullyClearedEvidence());

        Assert.False(afterWithdrawal.IsAllowed);
        Assert.True(afterWithdrawal.RefusedBecause(ReleaseRefusalReason.ReleaseIsWithdrawn));
    }

    [Fact]
    public void Refuse_RequiresAReason()
    {
        var silent = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReleaseDraft,
            ReleaseTransition.Refuse,
            Release,
            new ReleaseGateEvidence());

        Assert.False(silent.IsAllowed);
        Assert.True(silent.RefusedBecause(ReleaseRefusalReason.EvidenceIncomplete));

        var stated = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReleaseDraft,
            ReleaseTransition.Refuse,
            Release,
            new ReleaseGateEvidence { Reason = "Artifact hash mismatch against the build manifest." });

        Assert.True(stated.IsAllowed);
        Assert.Equal(ReleaseLifecycleState.Refused, stated.ToState);
    }
}

/// <summary>The migration and rollback classifications TASK 9 and TASK 10 require.</summary>
public sealed class ReleaseClassificationTests
{
    /// <summary>
    /// "No migrations were found" and "nobody looked" produce the same empty collection and demand opposite
    /// responses, so the no-migration state must state what it was established from.
    /// </summary>
    [Fact]
    public void NoDatabaseMigration_RequiresABasis_AndIsRecordedExplicitly()
    {
        var assessment = MigrationAssessment.NoDatabaseMigration("Inspected the published unit; no migrations assembly.");

        Assert.Equal(MigrationRequirementState.NoDatabaseMigration, assessment.State);
        Assert.True(assessment.IsNone);
        Assert.Equal(MigrationMetadata.None, assessment.Metadata);
        Assert.Equal("NO_DATABASE_MIGRATION", assessment.ToString());

        Assert.Throws<ArgumentException>(() => MigrationAssessment.NoDatabaseMigration("  "));
    }

    [Fact]
    public void MigrationsRequired_CarriesTheSetAndRefusesAnEmptyOne()
    {
        var assessment = MigrationAssessment.Required(
            new MigrationMetadata("ef-sqlserver", ["20260827064146_InitialSqlSchema"]),
            "0.0.0",
            "0.1.0",
            MigrationCompatibility.Behind,
            backupRequired: true,
            MigrationReversibility.ForwardFixOnly,
            "Read the migration directory inside the published unit.");

        Assert.Equal(MigrationRequirementState.MigrationsRequired, assessment.State);
        Assert.True(assessment.BackupRequired);
        Assert.Equal(MigrationReversibility.ForwardFixOnly, assessment.Reversibility);
        Assert.Equal("0.0.0", assessment.FromVersion);
        Assert.Equal("0.1.0", assessment.ToVersion);

        // Claiming a migration applies while naming none is the fabricated state, refused here.
        Assert.Throws<ArgumentException>(() => MigrationAssessment.Required(
            MigrationMetadata.None, null, null, MigrationCompatibility.Behind, false, MigrationReversibility.Unknown, "because"));
    }

    /// <summary>
    /// Unknown is a recorded state, not a failure of the release: defaulting to "none" would convert an
    /// unmeasured risk into a false assurance, which is the state W9.0's migration findings warn about.
    /// </summary>
    [Fact]
    public void Unestablished_RecordsWhy_AndRequiresABackup()
    {
        var assessment = MigrationAssessment.Unestablished(
            null,
            "The unit is published output; the migration set could not be read from it.");

        Assert.Equal(MigrationRequirementState.Unknown, assessment.State);
        Assert.True(assessment.BackupRequired);
        Assert.Equal(MigrationReversibility.Unknown, assessment.Reversibility);

        Assert.Throws<ArgumentException>(() => MigrationAssessment.Unestablished(null, string.Empty));
    }

    /// <summary>TASK 10: a first release records the typed absence rather than fabricating a predecessor.</summary>
    [Fact]
    public void RollbackMetadata_RecordsNoPreviousAcceptedRelease_WithoutInventingOne()
    {
        var rollback = ReleaseTestData.FirstRelease;

        Assert.Equal(RollbackReferenceState.NoPreviousAcceptedRelease, rollback.State);
        Assert.Null(rollback.PreviousReleaseId);
        Assert.Contains("NO_PREVIOUS_ACCEPTED_RELEASE", rollback.Summary, StringComparison.Ordinal);
        Assert.True(ReleaseTestData.Record().IsFirstRelease);

        Assert.Throws<ArgumentException>(() => RollbackMetadata.NoPreviousAcceptedRelease(false, "  "));
    }

    [Fact]
    public void RollbackMetadata_PointsAtThePreviousReleaseForALaterOne()
    {
        var previous = ReleaseTestData.Identity(digestFill: 'c').ReleaseId;
        var rollback = RollbackMetadata.ToPreviousRelease(previous, crossesMigrationBoundary: true, rehearsed: true, "Prior release accepted in ENV-DEV.");

        Assert.Equal(RollbackReferenceState.PreviousAcceptedReleaseExists, rollback.State);
        Assert.Equal(previous, rollback.PreviousReleaseId);
        Assert.True(rollback.CrossesMigrationBoundary);
        Assert.True(rollback.Rehearsed);

        // And it converts to the W9.1 rollback request, so a later stage consumes one mechanism, not two.
        var request = rollback.ToRequest(
            BundleId.Parse("nexus-2026.09.23-w93rel"),
            BundleId.Parse("nexus-2026.09.22-prior"),
            DeploymentEnvironmentId.TestEnv,
            previousBundleAvailable: true,
            DeploymentAuthorization.For(DeploymentAuthorityRole.Owner, "owner", DateTimeOffset.UnixEpoch));

        Assert.NotNull(request);
        Assert.True(request!.CrossesMigrationBoundary);
        Assert.True(request.Rehearsed);

        // A first release has nothing to plan a rollback against, and says so by returning nothing.
        Assert.Null(ReleaseTestData.FirstRelease.ToRequest(
            BundleId.Parse("nexus-2026.09.23-w93rel"), null, DeploymentEnvironmentId.TestEnv, false));
    }
}
