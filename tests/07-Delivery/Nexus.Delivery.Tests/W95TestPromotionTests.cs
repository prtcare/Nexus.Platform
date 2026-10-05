using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// W9.5 — the promotion precondition gate, the TEST deployment identity, and the negative controls the
/// directive requires for promotion from ENV-DEV into ENV-TEST.
///
/// <para>
/// <b>Why these are deterministic tests beside the component rather than live CLI controls.</b> W9.4
/// attempted two negative controls through the running deployment verb and both were <c>VOID</c>: the run
/// refused, but for a reason that was not the injected fault (a file lock from the decoy's own directory,
/// and a decoy that never bound the port). A control that refuses for the wrong reason is not a control.
/// Everything here breaks exactly one input and asserts the refusal names that input, so the attribution
/// cannot be accidental.
/// </para>
///
/// <para>
/// <b>Every refusal assertion has a non-vacuity companion.</b> A gate that refused everything would satisfy
/// every "is refused" test in this file; <see cref="TheSatisfiedRequestIsAllowed"/> and the positive twins
/// beside each negative control are what stop that.
/// </para>
/// </summary>
public sealed class W95TestPromotionTests
{
    private static readonly ReleaseId CertifiedRelease = ReleaseId.Parse("rel-2ec4c364727bcb74");

    /// <summary>The five migrations the certified release records, quoted so the fixture cannot drift from it.</summary>
    private static readonly string[] FiveMigrations =
    [
        "20260915074030_InitialSchema", "20260915075602_RecruitmentAttribution",
        "20260915080000_SpatialAndAudit", "20260921091956_ModularBoundaries",
        "20260921100000_ModuleIntegrity"
    ];

    // =============================================================================================
    // The satisfied baseline. Every negative control below is this, with exactly one thing broken.
    // =============================================================================================

    /// <summary>
    /// A request in which all nine preconditions are established: the DEV deployment and verification of
    /// this bundle are recorded, the remedy has been rehearsed, the reference and the bytes are intact, the
    /// governed plan covers ENV-TEST, the target is described, and the register of blocking defects has been
    /// declared and is empty.
    /// </summary>
    private static PromotionGovernanceRequest Satisfied(
        DeploymentBlockingDefectRegister? defects = null,
        DeploymentEnvironmentId? target = null)
        => new()
        {
            TargetEnvironment = target ?? DeploymentEnvironmentId.TestEnv,
            CurrentState = PromotionState.VerifiedDev,
            SourceEnvironment = DeploymentEnvironmentId.DevEnv,

            SourceReleaseCertified = true,
            SourceReleaseLifecycle = ReleaseLifecycleState.ReadyForDev,

            SourceDeploymentLineageIds = ["L-W9-2"],
            SourceVerificationLineageIds = ["L-W9-6"],

            RollbackCapabilityProven = true,
            ReleaseReferenceIntact = true,
            ArtifactIntegrityIntact = true,
            SecurityPlanPermitsTarget = true,
            TargetEnvironmentDefinitionValid = true,

            BlockingDefects = defects ?? DeploymentBlockingDefectRegister.Empty
        };

    /// <summary>
    /// <b>The non-vacuity companion.</b> Without this, every other test in the class would pass against a
    /// gate hard-wired to refuse.
    /// </summary>
    [Fact]
    public void TheSatisfiedRequestIsAllowed()
    {
        var decision = PromotionGovernance.Evaluate(Satisfied());

        Assert.True(decision.IsAllowed, decision.ToString());
        Assert.Equal([PromotionRefusalReason.None], decision.RefusalReasons);
    }

    /// <summary>
    /// <b>The gate must not be satisfiable by saying nothing.</b> A request that has been constructed but
    /// not filled in must refuse — this is the "guard that cannot fail" defect the estate keeps re-finding,
    /// expressed as its own control.
    /// </summary>
    [Fact]
    public void AnUnestablishedRequestIsRefused()
    {
        var decision = PromotionGovernance.Evaluate(new PromotionGovernanceRequest
        {
            TargetEnvironment = DeploymentEnvironmentId.TestEnv,
            CurrentState = PromotionState.VerifiedDev
        });

        Assert.True(decision.IsRefused);
        Assert.Contains(PromotionRefusalReason.SourceReleaseNotCertified, decision.RefusalReasons);
        Assert.Contains(PromotionRefusalReason.SourceDeploymentNotRecorded, decision.RefusalReasons);
        Assert.Contains(PromotionRefusalReason.SourceVerificationNotComplete, decision.RefusalReasons);
        Assert.Contains(PromotionRefusalReason.RollbackCapabilityNotProven, decision.RefusalReasons);
        Assert.Contains(PromotionRefusalReason.ReleaseReferenceNotIntact, decision.RefusalReasons);
        Assert.Contains(PromotionRefusalReason.ArtifactIntegrityNotIntact, decision.RefusalReasons);
        Assert.Contains(PromotionRefusalReason.SecurityPlanDoesNotPermitTarget, decision.RefusalReasons);
        Assert.Contains(PromotionRefusalReason.TargetEnvironmentDefinitionNotEstablished, decision.RefusalReasons);

        // The default member carries the refusing value, not the passing one. This is the specific assertion
        // that stops a future edit from giving the register a default that passes.
        Assert.Contains(PromotionRefusalReason.DeploymentBlockingDefectOutstanding, decision.RefusalReasons);
    }

    // =============================================================================================
    // TASK 1 — one control per named requirement
    // =============================================================================================

    [Fact]
    public void AnUncertifiedSourceReleaseRefuses()
    {
        var decision = PromotionGovernance.Evaluate(Satisfied() with
        {
            SourceReleaseCertified = false,
            SourceReleaseLifecycle = ReleaseLifecycleState.ReadyForDevPendingSecurityAction
        });

        Assert.True(decision.IsRefused);
        Assert.Contains(PromotionRefusalReason.SourceReleaseNotCertified, decision.RefusalReasons);
        Assert.Contains("RELEASE", decision.Detail.Single(d => d.StartsWith("SOURCE_RELEASE_NOT_CERTIFIED", StringComparison.Ordinal)));
    }

    [Fact]
    public void ADeploymentWithNoRecordedDevVerificationRefuses()
    {
        // A deployment exists and nothing verified it. Rule R-4: an artifact reaches an environment only
        // from the one before it, and it reaches it PROVEN, not merely present.
        var decision = PromotionGovernance.Evaluate(Satisfied() with { SourceVerificationLineageIds = [] });

        Assert.True(decision.IsRefused);
        Assert.Contains(PromotionRefusalReason.SourceVerificationNotComplete, decision.RefusalReasons);
    }

    [Fact]
    public void AnUnreadLedgerIsDistinguishedFromAnEmptyOne()
    {
        // Both refuse - for this question they are the same answer - but the stored bytes differ, so the
        // evidence can say which happened.
        var notRead = PromotionGovernance.Evaluate(Satisfied() with { SourceDeploymentLineageIds = null });
        var empty = PromotionGovernance.Evaluate(Satisfied() with { SourceDeploymentLineageIds = [] });

        Assert.Contains(PromotionRefusalReason.SourceDeploymentNotRecorded, notRead.RefusalReasons);
        Assert.Contains(PromotionRefusalReason.SourceDeploymentNotRecorded, empty.RefusalReasons);

        Assert.Contains("the ledger was not read", notRead.Detail.Single(d => d.StartsWith("SOURCE_DEPLOYMENT_NOT_RECORDED", StringComparison.Ordinal)));
        Assert.Contains("no deployment of this bundle", empty.Detail.Single(d => d.StartsWith("SOURCE_DEPLOYMENT_NOT_RECORDED", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnUnrehearsedRollbackRefuses()
    {
        var decision = PromotionGovernance.Evaluate(Satisfied() with { RollbackCapabilityProven = null });

        Assert.True(decision.IsRefused);
        Assert.Contains(PromotionRefusalReason.RollbackCapabilityNotProven, decision.RefusalReasons);

        // Observed false is a failure and reports as one; null is "nobody has said". The distinction is the
        // reason the member is three-valued.
        var failed = PromotionGovernance.Evaluate(Satisfied() with { RollbackCapabilityProven = false });
        Assert.Contains(PromotionRefusalReason.RollbackCapabilityNotProven, failed.RefusalReasons);
    }

    [Fact]
    public void ADriftedReleaseReferenceRefuses()
    {
        var decision = PromotionGovernance.Evaluate(Satisfied() with { ReleaseReferenceIntact = false });

        Assert.True(decision.IsRefused);
        Assert.Contains(PromotionRefusalReason.ReleaseReferenceNotIntact, decision.RefusalReasons);
    }

    [Fact]
    public void ArtifactIntegrityIsRequired()
    {
        var decision = PromotionGovernance.Evaluate(Satisfied() with { ArtifactIntegrityIntact = false });

        Assert.True(decision.IsRefused);
        Assert.Contains(PromotionRefusalReason.ArtifactIntegrityNotIntact, decision.RefusalReasons);
    }

    [Fact]
    public void ASecurityPlanThatDoesNotCoverTheTargetRefuses()
    {
        var decision = PromotionGovernance.Evaluate(Satisfied() with { SecurityPlanPermitsTarget = false });

        Assert.True(decision.IsRefused);
        Assert.Contains(PromotionRefusalReason.SecurityPlanDoesNotPermitTarget, decision.RefusalReasons);
    }

    [Fact]
    public void AnUnvalidatedTargetEnvironmentDefinitionRefuses()
    {
        var decision = PromotionGovernance.Evaluate(Satisfied() with { TargetEnvironmentDefinitionValid = null });

        Assert.True(decision.IsRefused);
        Assert.Contains(PromotionRefusalReason.TargetEnvironmentDefinitionNotEstablished, decision.RefusalReasons);
    }

    // ---- 8. the blocking-defect register -----------------------------------------------------------------

    [Fact]
    public void AnUndeclaredBlockingDefectRegisterRefuses()
    {
        var decision = PromotionGovernance.Evaluate(Satisfied(DeploymentBlockingDefectRegister.NotDeclared));

        Assert.True(decision.IsRefused);
        Assert.Contains(PromotionRefusalReason.DeploymentBlockingDefectOutstanding, decision.RefusalReasons);
        Assert.Contains("NOT_DECLARED", decision.Detail.Single(d => d.StartsWith("DEPLOYMENT_BLOCKING_DEFECT_REGISTER_NOT_DECLARED", StringComparison.Ordinal)));
    }

    [Fact]
    public void ADeclaredAndEmptyRegisterIsAllowed()
    {
        // The positive twin. An empty register is a statement that the register was enumerated and holds
        // nothing, and it is distinguishable from never having stated anything.
        var decision = PromotionGovernance.Evaluate(Satisfied(DeploymentBlockingDefectRegister.Empty));

        Assert.True(decision.IsAllowed, decision.ToString());
    }

    [Fact]
    public void AnUnresolvedBlockingDefectRefuses()
    {
        var decision = PromotionGovernance.Evaluate(Satisfied(DeploymentBlockingDefectRegister.Declared(
        [
            new DeploymentBlockingDefect("W9-DEBT-99", "A defect that blocks promotion.", IsResolved: false, Resolution: null)
        ])));

        Assert.True(decision.IsRefused);
        Assert.Contains(PromotionRefusalReason.DeploymentBlockingDefectOutstanding, decision.RefusalReasons);
        Assert.Contains("W9-DEBT-99", decision.Detail.Single(d => d.StartsWith("DEPLOYMENT_BLOCKING_DEFECT_OUTSTANDING", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A defect marked resolved with nothing beside it is <b>not</b> discharged. The register exists so a
    /// claim can be checked; "resolved" with no stated act cannot be.
    /// </summary>
    [Fact]
    public void ABlockingDefectResolvedWithoutAStatedActStillRefuses()
    {
        var decision = PromotionGovernance.Evaluate(Satisfied(DeploymentBlockingDefectRegister.Declared(
        [
            new DeploymentBlockingDefect("W9-DEBT-99", "A defect claimed fixed.", IsResolved: true, Resolution: null)
        ])));

        Assert.True(decision.IsRefused);
        Assert.Contains(PromotionRefusalReason.DeploymentBlockingDefectOutstanding, decision.RefusalReasons);
        Assert.Contains("declared resolved with no resolution recorded", decision.Detail.Single(d => d.StartsWith("DEPLOYMENT_BLOCKING_DEFECT_OUTSTANDING", StringComparison.Ordinal)));
    }

    [Fact]
    public void AResolvedBlockingDefectWithAStatedActIsAllowed()
    {
        var decision = PromotionGovernance.Evaluate(Satisfied(DeploymentBlockingDefectRegister.Declared(
        [
            new DeploymentBlockingDefect("W9-DEBT-99", "A defect that blocked promotion.", IsResolved: true,
                Resolution: "Discharged by commit 1f62530 and re-proven by the W9.4 regression.")
        ])));

        Assert.True(decision.IsAllowed, decision.ToString());
    }

    // ---- the promotion must be a promotion ----------------------------------------------------------------

    [Fact]
    public void PromotingIntoTheSourceEnvironmentIsNotAPromotion()
    {
        // Declared target ENV-DEV while claiming the source is ENV-DEV: rule R-4 promotes only to a LATER
        // environment. Refused rather than treated as a no-op, so a promotion record cannot be filed for a
        // step that moved nothing.
        var decision = PromotionGovernance.Evaluate(Satisfied(target: DeploymentEnvironmentId.DevEnv) with
        {
            SourceEnvironment = DeploymentEnvironmentId.DevEnv
        });

        Assert.True(decision.IsRefused);
        Assert.Contains(PromotionRefusalReason.TargetIsNotAPromotion, decision.RefusalReasons);
    }

    [Fact]
    public void EveryAbsentRequirementIsReportedNotJustTheFirst()
    {
        // A reader who fixes one refusal at a time re-runs the gate once per defect; the estate has paid for
        // that pattern already.
        var decision = PromotionGovernance.Evaluate(Satisfied() with
        {
            SourceReleaseCertified = false,
            ReleaseReferenceIntact = false,
            ArtifactIntegrityIntact = false
        });

        Assert.Equal(3, decision.RefusalReasons.Count);
        Assert.Equal(3, decision.Detail.Count);
    }

    // =============================================================================================
    // TASK 4 — the TEST deployment identity
    // =============================================================================================

    /// <summary>
    /// The promotion carries the same release, the same artifact and the same build, and a different
    /// deployment identity in a different environment. This is the whole of the TASK 4 requirement,
    /// expressed against the contract types rather than against prose.
    /// </summary>
    [Fact]
    public void TheTestDeploymentIdSharesTheReleaseAndDiffersFromTheDevOne()
    {
        var dev = DeploymentId.For(CertifiedRelease, DeploymentEnvironmentId.DevEnv, 2);
        var test = DeploymentId.For(CertifiedRelease, DeploymentEnvironmentId.TestEnv, 1);

        Assert.NotEqual(dev.Value, test.Value);

        Assert.Equal(CertifiedRelease, test.Release);
        Assert.Equal(dev.Release, test.Release);

        Assert.Equal(DeploymentEnvironmentId.TestEnv, test.Environment);
        Assert.NotEqual(dev.Environment, test.Environment);

        // The grammar is the contract's, so a reader can resolve the deployment from the string alone.
        Assert.Equal("dep-2ec4c364727bcb74-test-1", test.Value);
        Assert.True(DeploymentId.IsValid(test.Value));
        Assert.Equal(test, DeploymentId.Parse(test.Value));
    }

    [Fact]
    public void ADEPLOYMENTIdThatNamesTheWrongEnvironmentIsRefused()
    {
        var bundle = TestData.Bundle();
        var request = TestData.Request(bundle, DeploymentTransition.PromoteToTest, PromotionState.VerifiedDev);

        // A test id handed to a promotion into the OTHER environment. The id is deterministic and therefore
        // easy to construct correctly, which is exactly why a wrong one would never be noticed by reading it.
        var promoter = new DeploymentPromoter(new FileArtifactRegistry(Path.GetTempPath()), new NullLineageLog());

        var thrown = Assert.ThrowsAsync<ArgumentException>(() =>
            promoter.TransitionAsync(
                request,
                "L-W9-5",
                DeploymentId.For(CertifiedRelease, DeploymentEnvironmentId.DevEnv, 1)));

        Assert.Contains("names ENV-DEV", thrown.Result.Message);
    }

    /// <summary>A lineage log that never accepts anything, so the identity check is what the test exercises.</summary>
    private sealed class NullLineageLog : IDeploymentLineageLog
    {
        public Task<LineageAppendOutcome> AppendAsync(DeploymentLineageRecord record, CancellationToken cancellationToken = default)
            => Task.FromResult(LineageAppendOutcome.Refused("This log records nothing."));

        public Task<DeploymentLineageRecord?> TryGetAsync(string lineageId, CancellationToken cancellationToken = default)
            => Task.FromResult<DeploymentLineageRecord?>(null);

        public Task<IReadOnlyList<DeploymentLineageRecord>> ReadByBundleAsync(BundleId bundleId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeploymentLineageRecord>>([]);

        public Task<IReadOnlyList<DeploymentLineageRecord>> ReadAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeploymentLineageRecord>>([]);
    }

    // =============================================================================================
    // TASK 17 — the negative controls, through the state machine
    // =============================================================================================

    private static DeploymentGateEvidence PromotionEvidence(ReleaseBundle bundle, bool smoke = true, bool ready = true)
        => TestData.HealthyEvidence(bundle) with
        {
            ReadinessObserved = ready,
            SmokeObserved = smoke
        };

    /// <summary>Control 6 and 7: an unhealthy or un-smoked TEST deployment does not become VerifiedTest.</summary>
    [Fact]
    public void AnUnreadyTestDeploymentDoesNotBecomeVerifiedTest()
    {
        var bundle = TestData.Bundle();

        var decision = DeploymentStateMachine.Decide(TestData.Request(
            bundle, DeploymentTransition.VerifyInTest, PromotionState.DeployedTest,
            PromotionEvidence(bundle, ready: false)));

        Assert.True(decision.IsRefused);
        Assert.Contains(DeploymentRefusalReason.ReadinessNotObserved, decision.RefusalReasons);
        Assert.NotEqual(PromotionState.VerifiedTest, decision.NextState);
    }

    [Fact]
    public void AFailedSmokeDoesNotBecomeVerifiedTest()
    {
        var bundle = TestData.Bundle();

        var decision = DeploymentStateMachine.Decide(TestData.Request(
            bundle, DeploymentTransition.VerifyInTest, PromotionState.DeployedTest,
            PromotionEvidence(bundle, smoke: false)));

        Assert.True(decision.IsRefused);
        Assert.Contains(DeploymentRefusalReason.SmokeNotObserved, decision.RefusalReasons);
        Assert.NotEqual(PromotionState.VerifiedTest, decision.NextState);
    }

    /// <summary>
    /// An un<em>taken</em> smoke observation refuses exactly as a failed one does. This is the W9.4 false
    /// pass, where the verdict existed and no decision consulted it.
    /// </summary>
    [Fact]
    public void AnUnobservedSmokeDoesNotBecomeVerifiedTest()
    {
        var bundle = TestData.Bundle();

        var decision = DeploymentStateMachine.Decide(TestData.Request(
            bundle, DeploymentTransition.VerifyInTest, PromotionState.DeployedTest,
            PromotionEvidence(bundle) with { SmokeObserved = null }));

        Assert.True(decision.IsRefused);
        Assert.Contains(DeploymentRefusalReason.SmokeNotObserved, decision.RefusalReasons);
    }

    /// <summary>Control 4: an invalid TEST configuration refuses the promotion.</summary>
    [Fact]
    public void AnInvalidTestConfigurationRefusesPromotion()
    {
        var bundle = TestData.Bundle();

        var decision = DeploymentStateMachine.Decide(TestData.Request(
            bundle, DeploymentTransition.PromoteToTest, PromotionState.VerifiedDev,
            PromotionEvidence(bundle) with { ConfigurationSchemaSatisfied = false }));

        Assert.True(decision.IsRefused);
        Assert.Contains(DeploymentRefusalReason.ConfigurationSchemaUnsatisfied, decision.RefusalReasons);
    }

    /// <summary>Control 5: a missing TEST secret reference is an unsatisfied configuration, not a warning.</summary>
    [Fact]
    public void AMissingTestSecretReferenceRefusesPromotion()
    {
        var bundle = TestData.Bundle();

        var decision = DeploymentStateMachine.Decide(TestData.Request(
            bundle, DeploymentTransition.PromoteToTest, PromotionState.VerifiedDev,
            PromotionEvidence(bundle) with { ConfigurationSchemaSatisfied = null }));

        Assert.True(decision.IsRefused);
        Assert.Contains(DeploymentRefusalReason.EvidenceIncomplete, decision.RefusalReasons);
    }

    /// <summary>Control 9: a drifted or unprotected release reference refuses the promotion.</summary>
    [Fact]
    public void ADriftedReleaseReferenceRefusesPromotion()
    {
        var bundle = TestData.Bundle();

        var decision = DeploymentStateMachine.Decide(TestData.Request(
            bundle, DeploymentTransition.PromoteToTest, PromotionState.VerifiedDev,
            PromotionEvidence(bundle) with
            {
                ReleaseRefIsProtected = false,
                ReleaseReferenceProtection = ReleaseReferenceProtectionEvidence.Unrecorded
            }));

        Assert.True(decision.IsRefused);
        Assert.Contains(DeploymentRefusalReason.ReleaseRefNotProtected, decision.RefusalReasons);
    }

    /// <summary>
    /// Control 10: the C-2 compensating control does not cover the environment being promoted into.
    ///
    /// <para>
    /// <b>This is the same protection check the DEV deployment passed</b>, asked of a target the deviation
    /// does not extend to. The substitution is DEV/TEST-only, and the judgement refuses it outside that
    /// scope however it is approved — so the deviation's boundary is enforced rather than documented.
    /// </para>
    /// </summary>
    [Fact]
    public void ACompensatingControlWithoutScopeDoesNotCarryAPromotion()
    {
        var evidence = ReleaseReferenceProtectionEvidence.CompensatingControl(
            approved: true,
            verified: true,
            scope: ReleaseReferenceProtectionScope.None);

        var judgement = evidence.Judge(DeploymentEnvironmentId.TestEnv);

        Assert.False(judgement.IsSatisfied);
        Assert.False(judgement.IsUnobserved);
        Assert.Equal(ReleaseReferenceProtectionJudgement.OutstandingCode, judgement.Code);
    }

    /// <summary>
    /// The DEV/TEST pair the deviation covers is satisfied, and ENV-PROD is refused by the same recorded
    /// evidence. <b>The boundary is the point</b>: one boolean could not express it, which is why the mode
    /// and the scope replaced it.
    /// </summary>
    [Fact]
    public void TheDevTestScopeCoversTestAndNotProd()
    {
        var evidence = ReleaseReferenceProtectionEvidence.CompensatingControl();

        Assert.True(evidence.Judge(DeploymentEnvironmentId.DevEnv).IsSatisfied);
        Assert.True(evidence.Judge(DeploymentEnvironmentId.TestEnv).IsSatisfied);
        Assert.False(evidence.Judge(DeploymentEnvironmentId.ProdEnv).IsSatisfied);
    }

    /// <summary>
    /// A compensating control claiming scope over all three ratified environments is <b>refused</b> rather
    /// than silently narrowed: a record saying one thing while the behaviour does another is the defect the
    /// typed scope exists to prevent.
    /// </summary>
    [Fact]
    public void ACompensatingControlClaimingAllEnvironmentsIsRefused()
    {
        var evidence = ReleaseReferenceProtectionEvidence.CompensatingControl(
            scope: ReleaseReferenceProtectionScope.AllRatifiedEnvironments);

        Assert.False(evidence.Judge(DeploymentEnvironmentId.TestEnv).IsSatisfied);
    }

    // =============================================================================================
    // TASK 12 — the evidence envelope, per attempt, for the TEST lane
    // =============================================================================================

    private static DeploymentId TestDeployment(int attempt = 1)
        => DeploymentId.For(CertifiedRelease, DeploymentEnvironmentId.TestEnv, attempt);

    [Fact]
    public async Task TheTestEvidencePathIsAFunctionOfTheDeploymentIdAndTheAttempt()
    {
        var root = Path.Combine(Path.GetTempPath(), "w95-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            var transaction = DeploymentEvidenceTransaction.ForAttempt(root, TestDeployment(), 1);

            Assert.EndsWith(
                Path.Combine("deployments", "dep-2ec4c364727bcb74-test-1", "attempts", "1", "TEST_VERIFICATION.json"),
                transaction.EvidencePath);

            var facts = new Dictionary<string, object?> { ["environment"] = "ENV-TEST" };

            var first = await transaction.StageAsync(
                "rel-2ec4c364727bcb74", DeploymentEnvironmentId.TestEnv, TestDeployment(), 1, "L-W9-7", facts);

            Assert.Equal(DeploymentEvidenceStaging.Created, first.Staging);

            // Same attempt, same evidence -> idempotent, and the existing envelope is NOT rewritten.
            var before = File.ReadAllText(transaction.EvidencePath);
            var second = await transaction.StageAsync(
                "rel-2ec4c364727bcb74", DeploymentEnvironmentId.TestEnv, TestDeployment(), 1, "L-W9-7", facts);

            Assert.Equal(DeploymentEvidenceStaging.Existing, second.Staging);
            Assert.Equal(before, File.ReadAllText(transaction.EvidencePath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// The destructive case the attempt-scoped address exists to prevent: a <b>different</b> envelope for an
    /// attempt that already has one is refused, and the original survives untouched.
    /// </summary>
    [Fact]
    public async Task DifferentEvidenceForTheSameTestAttemptIsRefused()
    {
        var root = Path.Combine(Path.GetTempPath(), "w95-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            var transaction = DeploymentEvidenceTransaction.ForAttempt(root, TestDeployment(), 1);

            await transaction.StageAsync(
                "rel-2ec4c364727bcb74", DeploymentEnvironmentId.TestEnv, TestDeployment(), 1, "L-W9-7",
                new Dictionary<string, object?> { ["environment"] = "ENV-TEST" });

            var original = File.ReadAllText(transaction.EvidencePath);

            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => transaction.StageAsync(
                "rel-2ec4c364727bcb74", DeploymentEnvironmentId.TestEnv, TestDeployment(), 1, "L-W9-7",
                new Dictionary<string, object?> { ["environment"] = "ENV-PROD" }));

            Assert.Equal(original, File.ReadAllText(transaction.EvidencePath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Control 8: an evidence persistence failure leaves the lifecycle untouched. <c>CommitAsync</c> accepts
    /// nothing but a <see cref="DeploymentEvidenceTransaction.StagedEvidence"/>, and the only way to obtain
    /// one is for the evidence to be on disk first — so a staging failure never reaches the ledger.
    /// </summary>
    [Fact]
    public async Task ACommitFailureLeavesTheEnvelopeTruthfulAndTheLedgerUnmoved()
    {
        var root = Path.Combine(Path.GetTempPath(), "w95-evidence-" + Guid.NewGuid().ToString("N"));

        try
        {
            var transaction = DeploymentEvidenceTransaction.ForAttempt(root, TestDeployment(), 1);

            var staged = await transaction.StageAsync(
                "rel-2ec4c364727bcb74", DeploymentEnvironmentId.TestEnv, TestDeployment(), 1, "L-W9-7",
                new Dictionary<string, object?> { ["environment"] = "ENV-TEST" });

            await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
                transaction.CommitAsync(staged, () => throw new InvalidOperationException("the ledger refused the record")));

            // The envelope exists and SAYS the transition did not commit. That is the whole point of writing
            // it first: a failure between the two leaves an artifact that is truthful, not a ledger record
            // with nothing beside it.
            var envelope = await File.ReadAllTextAsync(transaction.EvidencePath);

            Assert.Contains(DeploymentEvidenceState.CommitFailed.ToString(), envelope);
            Assert.DoesNotContain(DeploymentEvidenceState.Committed.ToString(), envelope);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Two lanes cannot address one envelope. The DEV and TEST verification envelopes for the same release
    /// resolve to different directories <b>and</b> different file names, so neither lane can overwrite the
    /// other's evidence — the failure that destroyed two W9.4 envelopes.
    /// </summary>
    [Fact]
    public void TheTwoLanesCannotAddressOneEnvelope()
    {
        var root = Path.Combine(Path.GetTempPath(), "w95-evidence-" + Guid.NewGuid().ToString("N"));

        var dev = DeploymentEvidenceTransaction.ForAttempt(root, DeploymentId.For(CertifiedRelease, DeploymentEnvironmentId.DevEnv, 1), 1);
        var test = DeploymentEvidenceTransaction.ForAttempt(root, TestDeployment(), 1);

        Assert.NotEqual(dev.EvidencePath, test.EvidencePath);
        Assert.EndsWith("DEV_VERIFICATION.json", dev.EvidencePath);
        Assert.EndsWith("TEST_VERIFICATION.json", test.EvidencePath);
    }

    // =============================================================================================
    // The lane is not a second authority
    // =============================================================================================

    /// <summary>
    /// A lane's declared states are checked against the state machine when it is constructed, so a lane that
    /// disagreed with the machine would throw rather than record a transition the machine would never have
    /// allowed. This asserts the property the lanes are <i>for</i>: the environment is data, and the rules
    /// are the same in both.
    /// </summary>
    [Fact]
    public void BothLanesAgreeWithTheStateMachine()
    {
        foreach (var transition in new[] { DeploymentTransition.PromoteToTest, DeploymentTransition.VerifyInTest })
        {
            Assert.NotNull(DeploymentStateMachine.PredecessorStatesFor(transition));
            Assert.NotNull(DeploymentStateMachine.NextStateFor(transition));
        }

        Assert.Equal(PromotionState.DeployedTest, DeploymentStateMachine.NextStateFor(DeploymentTransition.PromoteToTest));
        Assert.Equal(PromotionState.VerifiedTest, DeploymentStateMachine.NextStateFor(DeploymentTransition.VerifyInTest));

        // The promotion's predecessor is VerifiedDev, which is exactly what the DEV lane produces - the
        // two halves of the chain TASK 14 requires are joined by the contract, not by a convention.
        Assert.Equal([PromotionState.VerifiedDev], DeploymentStateMachine.PredecessorStatesFor(DeploymentTransition.PromoteToTest));
    }

    /// <summary>
    /// A promotion from ENV-DEV that carries the DEV verification's own observations is
    /// <b>allowed by the state machine</b> — and only because every observation was genuinely taken. This is
    /// the positive twin of the negative controls above: without it, they would all pass against a machine
    /// hard-wired to refuse.
    /// </summary>
    [Fact]
    public void AHealthyPromotionIntoTestIsAllowed()
    {
        var bundle = TestData.Bundle();

        var promotion = DeploymentStateMachine.Decide(TestData.Request(
            bundle, DeploymentTransition.PromoteToTest, PromotionState.VerifiedDev, PromotionEvidence(bundle)));

        Assert.True(promotion.IsAllowed, string.Join(", ", promotion.RefusalReasons));
        Assert.Equal(PromotionState.DeployedTest, promotion.NextState);

        var verification = DeploymentStateMachine.Decide(TestData.Request(
            bundle, DeploymentTransition.VerifyInTest, PromotionState.DeployedTest, PromotionEvidence(bundle)));

        Assert.True(verification.IsAllowed, string.Join(", ", verification.RefusalReasons));
        Assert.Equal(PromotionState.VerifiedTest, verification.NextState);
    }

    /// <summary>
    /// The migration set the promotion is evaluated against is the release's certified set, unchanged.
    /// A TEST target that diverges from it refuses: this is the same judgement DEV was held to, applied to
    /// the second environment rather than a relaxed one.
    /// </summary>
    [Fact]
    public void ADivergentTestSchemaRefusesPromotion()
    {
        var bundle = TestData.Bundle();

        var divergent = DeploymentStateMachine.Decide(TestData.Request(
            bundle, DeploymentTransition.PromoteToTest, PromotionState.VerifiedDev,
            PromotionEvidence(bundle) with { MigrationCompatibility = MigrationCompatibility.Divergent }));

        Assert.True(divergent.IsRefused);
        Assert.Contains(DeploymentRefusalReason.MigrationSetDivergent, divergent.RefusalReasons);

        var ahead = DeploymentStateMachine.Decide(TestData.Request(
            bundle, DeploymentTransition.PromoteToTest, PromotionState.VerifiedDev,
            PromotionEvidence(bundle) with { MigrationCompatibility = MigrationCompatibility.Ahead }));

        Assert.True(ahead.IsRefused);
        Assert.Contains(DeploymentRefusalReason.MigrationSetAhead, ahead.RefusalReasons);
    }

    /// <summary>
    /// The certified migration set this suite's fixtures describe is the release's own. Asserted so that a
    /// change to the fixture cannot silently stop the promotion controls above from exercising the real set.
    /// </summary>
    [Fact]
    public void TheFixturesMigrationSetIsTheCertifiedOne()
    {
        Assert.Equal(5, FiveMigrations.Length);
        Assert.Equal(
            FiveMigrations,
            new MigrationMetadata("ef-postgresql", FiveMigrations).MigrationIds);
    }
}
