using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// TASK 6 and TASK 7 — the controls the W9.4 remediation added, and the deployment identity it introduced.
///
/// <para>
/// The three defect reproductions live in <see cref="W94InstrumentDefectReproductionTests"/>; these are the
/// gates that must hold once the fixes are in, including the non-vacuity companions that stop a gate which
/// refuses everything from looking green.
/// </para>
/// </summary>
public sealed class W94RemediationGateTests
{
    /// <summary>The deployment attempt these controls stage evidence for.</summary>
    private static DeploymentId EvidenceDeployment()
        => DeploymentId.For(ReleaseId.Parse("rel-2ec4c364727bcb74"), DeploymentEnvironmentId.DevEnv, 1);

    private static readonly string[] FiveMigrations =
    [
        "20260915074030_InitialSchema", "20260915075602_RecruitmentAttribution",
        "20260915080000_SpatialAndAudit", "20260921091956_ModularBoundaries",
        "20260921100000_ModuleIntegrity"
    ];

    private static MigrationAssessment BackupRequiredAssessment() => MigrationAssessment.Required(
        new MigrationMetadata("ef-postgresql", FiveMigrations),
        "0.0.0", "0.1.0", MigrationCompatibility.Match,
        backupRequired: true, reversibility: MigrationReversibility.ForwardFixOnly,
        basis: "BackupRequired=true as the release records.");

    private static DeploymentAuthorization DeliveryTeam() =>
        DeploymentAuthorization.For(DeploymentAuthorityRole.DeliveryTeam, "w9.4-remediation", DateTimeOffset.UnixEpoch);

    // =============================================================================================
    // TASK 6 — required backup missing, and statistics that were reset or stale
    // =============================================================================================

    /// <summary>
    /// <b>The exact defect shape.</b> A target reported empty on a basis that is only a planner statistic,
    /// against a release recording <c>BackupRequired = true</c>. The judgement must refuse to discharge the
    /// obligation and must say which basis it refused on — a refusal a reader cannot attribute is one they
    /// will attribute to the wrong thing.
    /// </summary>
    [Fact]
    public void T6_AnEmptyReadFromStatistics_IsRefusedWithATypedReason()
    {
        var observed = new MigrationTargetObservation(
            DeploymentEnvironmentId.DevEnv, reachable: true, schemaPresent: true,
            appliedMigrationIds: FiveMigrations,
            dataState: MigrationTargetDataState.Empty,
            backupVerified: false,
            basis: "summed their live row estimates: 0.",
            dataStateBasis: MigrationTargetDataStateBasis.StatisticsEstimate);

        var verdict = MigrationGovernance.Judge(new MigrationGovernanceRequest(
            BackupRequiredAssessment(), DeploymentEnvironmentId.DevEnv, observed, [], DeliveryTeam()));

        Assert.Equal(MigrationGovernanceDecision.BackupRequired, verdict.Decision);
        Assert.Contains(MigrationGovernanceRefusalReason.EmptinessNotDeterministicallyProven, verdict.RefusalReasons);
        Assert.False(verdict.PermitsDeployment);
    }

    /// <summary>
    /// The default basis is <c>Unestablished</c>, so a caller that reports empty and does not say how it
    /// knows is refused by the same rule. This is the case that catches a future observer which simply
    /// forgets to state its method — the failure mode a required parameter would miss.
    /// </summary>
    [Fact]
    public void T6_AnEmptyWithNoStatedBasis_IsRefused()
    {
        var observed = new MigrationTargetObservation(
            DeploymentEnvironmentId.DevEnv, reachable: true, schemaPresent: true,
            appliedMigrationIds: FiveMigrations,
            dataState: MigrationTargetDataState.Empty,
            backupVerified: false,
            basis: "The target is empty.");

        var verdict = MigrationGovernance.Judge(new MigrationGovernanceRequest(
            BackupRequiredAssessment(), DeploymentEnvironmentId.DevEnv, observed, [], DeliveryTeam()));

        Assert.Equal(MigrationGovernanceDecision.BackupRequired, verdict.Decision);
    }

    /// <summary>
    /// <b>The non-vacuity companion.</b> A deterministically measured empty target DOES discharge the
    /// obligation — otherwise the two tests above would pass on a rule that refuses all emptiness, which
    /// would train its readers to work around it. An obligation discharged by measurement is the outcome
    /// the policy exists to make possible.
    /// </summary>
    [Fact]
    public void T6_AnEmptyEstablishedByAnExactCount_DoesDischargeTheObligation()
    {
        var observed = new MigrationTargetObservation(
            DeploymentEnvironmentId.DevEnv, reachable: true, schemaPresent: true,
            appliedMigrationIds: FiveMigrations,
            dataState: MigrationTargetDataState.Empty,
            backupVerified: false,
            basis: "Exact total across all 27 table(s): 0.",
            dataStateBasis: MigrationTargetDataStateBasis.DeterministicCount);

        var verdict = MigrationGovernance.Judge(new MigrationGovernanceRequest(
            BackupRequiredAssessment(), DeploymentEnvironmentId.DevEnv, observed, [], DeliveryTeam()));

        Assert.Equal(MigrationGovernanceDecision.MigrationApproved, verdict.Decision);
        Assert.True(verdict.PermitsDeployment);
    }

    /// <summary>
    /// A count that could not be completed is <c>Unknown</c>, and an Unknown target keeps its obligation.
    /// Silence must not be the cheapest way to discharge a backup.
    /// </summary>
    [Fact]
    public void T6_AnUnestablishedCount_KeepsTheBackupObligation()
    {
        var observed = new MigrationTargetObservation(
            DeploymentEnvironmentId.DevEnv, reachable: true, schemaPresent: true,
            appliedMigrationIds: FiveMigrations,
            dataState: MigrationTargetDataStatePolicy.ClassifyFromDeterministicCount(null),
            backupVerified: false,
            basis: "The exact count of \"Ledger\" returned '(unreadable)'.",
            dataStateBasis: MigrationTargetDataStatePolicy.BasisFor(null));

        var verdict = MigrationGovernance.Judge(new MigrationGovernanceRequest(
            BackupRequiredAssessment(), DeploymentEnvironmentId.DevEnv, observed, [], DeliveryTeam()));

        Assert.Equal(MigrationGovernanceDecision.BackupRequired, verdict.Decision);
        Assert.Contains(MigrationGovernanceRefusalReason.TargetDataStateUnknown, verdict.RefusalReasons);
    }

    // =============================================================================================
    // TASK 6 — smoke and start state
    // =============================================================================================

    /// <summary>
    /// <b>The exact former false-pass condition, as a negative test.</b> During the W9.4 negative-control
    /// battery a second application instance could not bind the port and exited; the readiness probe was
    /// answered by the process already holding it; and the driver's own log capture recorded
    /// <c>logLinesCaptured=0, processSurvivedSmoke=false</c>. The run then transitioned to <c>VerifiedDev</c>.
    ///
    /// <para>
    /// This is that state: readiness observed, smoke observed false. It must not reach <c>VerifiedDev</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void T6_StartedFalse_CannotReachVerifiedDev()
    {
        var bundle = TestData.Bundle();

        var decision = DeploymentStateMachine.Decide(TestData.Request(
            bundle,
            DeploymentTransition.VerifyInDev,
            PromotionState.DeployedDev,
            TestData.HealthyEvidence(bundle) with { ReadinessObserved = true, SmokeObserved = false }));

        Assert.True(decision.IsRefused);
        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.SmokeNotObserved));
        Assert.NotEqual(PromotionState.VerifiedDev, decision.NextState);
    }

    /// <summary>
    /// The same requirement in ENV-PROD, because the gate is in the shared contract and a fix that only
    /// covered DEV would be a fix to the W9.4 driver rather than to the estate.
    /// </summary>
    [Fact]
    public void T6_VerifyInProd_AlsoRequiresSmoke()
    {
        var bundle = TestData.Bundle();

        var refused = DeploymentStateMachine.Decide(new PromotionRequest(
            bundle,
            DeploymentEnvironmentId.ProdEnv,
            PromotionState.DeployedProd,
            DeploymentTransition.VerifyInProd,
            TestData.HealthyEvidence(bundle, DeploymentAuthorityRole.Owner) with { SmokeObserved = false }));

        Assert.True(refused.IsRefused);
        Assert.True(refused.RefusedBecause(DeploymentRefusalReason.SmokeNotObserved));
    }

    // =============================================================================================
    // TASK 6 — evidence persistence and teardown, as gates rather than as prose
    // =============================================================================================

    /// <summary>
    /// A committed envelope names the lineage record it authorised. Without this the transaction could write
    /// a file and make no connection between it and the ledger, which is the state it exists to prevent.
    /// </summary>
    [Fact]
    public async Task T6_ACommittedEnvelope_NamesTheLineageRecordItAuthorised()
    {
        using var scratch = new TempDirectory("nexus-w94-t6-");
        var transaction = DeploymentEvidenceTransaction.ForAttempt(scratch.Path, EvidenceDeployment(), 1);

        var staged = await transaction.StageAsync(
            "rel-2ec4c364727bcb74", DeploymentEnvironmentId.DevEnv, EvidenceDeployment(), 1, "L-W9-9",
            new Dictionary<string, object?> { ["verified"] = true });

        Assert.Equal(DeploymentEvidenceState.PendingTransition, transaction.Read(staged.Path).State);

        var sealed_ = await transaction.CommitAsync(staged, () => Task.FromResult("L-W9-9"));

        Assert.Equal(DeploymentEvidenceState.Committed, sealed_.State);
        Assert.Equal("L-W9-9", sealed_.LineageId);

        // Read back from DISK, not from the returned value: the guarantee is about the file.
        var reRead = transaction.Read(staged.Path);

        Assert.Equal(DeploymentEvidenceState.Committed, reRead.State);
        Assert.Equal("L-W9-9", reRead.LineageId);
        Assert.Equal(DeploymentEvidenceState.Committed, reRead.State);
    }

    /// <summary>
    /// The two envelopes a pending and a committed record produce must not digest the same. A digest that
    /// ignored the state would let a reader treat an uncommitted deployment as a committed one.
    /// </summary>
    [Fact]
    public async Task T6_PendingAndCommittedEnvelopes_DoNotShareADigest()
    {
        using var scratch = new TempDirectory("nexus-w94-t6-digest-");
        var transaction = DeploymentEvidenceTransaction.ForAttempt(scratch.Path, EvidenceDeployment(), 1);

        var staged = await transaction.StageAsync(
            "rel-2ec4c364727bcb74", DeploymentEnvironmentId.DevEnv, EvidenceDeployment(), 1, "L-W9-9",
            new Dictionary<string, object?> { ["verified"] = true });

        var pending = transaction.Read(staged.Path).ComputeDigest();

        await transaction.CommitAsync(staged, () => Task.FromResult("L-W9-9"));

        var committed = transaction.Read(staged.Path).ComputeDigest();

        Assert.NotEqual(pending, committed);
    }

    // =============================================================================================
    // TASK 7 — deployment identity
    // =============================================================================================

    /// <summary>Same release, same environment, different attempt ⇒ different <c>DeploymentId</c>.</summary>
    [Fact]
    public void T7_TheSameReleaseAndEnvironment_DifferentAttempts_HaveDifferentDeploymentIds()
    {
        var release = ReleaseId.Parse("rel-2ec4c364727bcb74");

        var first = DeploymentId.For(release, DeploymentEnvironmentId.DevEnv, 1);
        var second = DeploymentId.For(release, DeploymentEnvironmentId.DevEnv, 2);

        Assert.NotEqual(first, second);
        Assert.Equal("dep-2ec4c364727bcb74-dev-1", first.Value);
        Assert.Equal("dep-2ec4c364727bcb74-dev-2", second.Value);
    }

    /// <summary>
    /// The identity is <b>deterministic</b>: two hosts computing it for the same attempt agree, which is what
    /// lets evidence written before a deployment state the id it will carry.
    /// </summary>
    [Fact]
    public void T7_TheSameAttempt_ProducesTheSameIdEveryTime()
    {
        var release = ReleaseId.Parse("rel-2ec4c364727bcb74");

        Assert.Equal(
            DeploymentId.For(release, DeploymentEnvironmentId.DevEnv, 3).Value,
            DeploymentId.For(release, DeploymentEnvironmentId.DevEnv, 3).Value);
    }

    /// <summary>A different environment is a different deployment, and the id says so.</summary>
    [Fact]
    public void T7_TheSameReleaseInDifferentEnvironments_HasDifferentDeploymentIds()
    {
        var release = ReleaseId.Parse("rel-2ec4c364727bcb74");

        Assert.NotEqual(
            DeploymentId.For(release, DeploymentEnvironmentId.DevEnv, 1),
            DeploymentId.For(release, DeploymentEnvironmentId.TestEnv, 1));
    }

    /// <summary>
    /// <b><c>LineageId</c> is not <c>DeploymentId</c>.</b> They are different grammars for different things —
    /// one numbers an audit record, the other names a deployment — and an id that satisfied both would let
    /// them be substituted for one another again.
    /// </summary>
    [Fact]
    public void T7_TheLineageIdGrammarAndTheDeploymentIdGrammarAreDistinct()
    {
        Assert.False(DeploymentId.IsValid("L-W9-2"));
        Assert.False(DeploymentId.IsValid("L-W9-2-verify"));
        Assert.True(DeploymentId.IsValid("dep-2ec4c364727bcb74-dev-2"));

        // And a deployment id is not a lineage id.
        Assert.False(DeploymentLineageRecord.IsValidLineageId("dep-2ec4c364727bcb74-dev-2"));
        Assert.True(DeploymentLineageRecord.IsValidLineageId("L-W9-2"));
    }

    [Fact]
    public void T7_DeploymentId_RefusesAnAttemptNumberThatNeverHappened()
    {
        var release = ReleaseId.Parse("rel-2ec4c364727bcb74");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => DeploymentId.For(release, DeploymentEnvironmentId.DevEnv, 0));
    }

    [Fact]
    public void T7_DeploymentId_RoundTripsThroughItsOwnGrammar()
    {
        var original = DeploymentId.For(ReleaseId.Parse("rel-2ec4c364727bcb74"), DeploymentEnvironmentId.DevEnv, 12);

        Assert.True(DeploymentId.TryParse(original.Value, out var parsed));
        Assert.Equal(original, parsed);
        Assert.Equal(original.Release, parsed!.Release);
        Assert.Equal(original.Environment, parsed.Environment);
        Assert.Equal(original.Attempt, parsed.Attempt);
    }

    /// <summary>
    /// <b>TASK 7 — the reverse lineage.</b> From a <c>DeploymentId</c>: the release and environment come out
    /// of the id itself, and the rest is walked through the governed ledger — the record names the artifacts,
    /// the source commit and the bundle.
    ///
    /// <para>
    /// The chain is <c>DeploymentId → ReleaseId → ArtifactId → BuildId → SourceCommit</c>. The final hop to
    /// the WorkItem is the run plan's <c>governedWorkReference</c>, which the ledger deliberately does not
    /// carry — a lineage record holds deployment facts, and the work item is a fact about the work, recorded
    /// in the governed workbook row for the same <c>LineageId</c>. This test walks what the ledger holds and
    /// asserts the LedgerId that carries the last hop.
    /// </para>
    /// </summary>
    [Fact]
    public async Task T7_ReverseLineage_IsWalkableFromTheDeploymentId()
    {
        using var scratch = new TempDirectory("nexus-w94-t7-");
        var ledger = new FileDeploymentLineageLog(scratch.Path);
        var bundle = TestData.Bundle();
        var deploymentId = DeploymentId.For(
            ReleaseId.Parse("rel-2ec4c364727bcb74"), DeploymentEnvironmentId.TestEnv, 1);

        var promoter = new DeploymentPromoter(new FileArtifactRegistry(System.IO.Path.Combine(scratch.Path, "registry")), ledger);

        var outcome = await promoter.TransitionAsync(
            TestData.Request(bundle, DeploymentTransition.Register, PromotionState.Built),
            "L-W9-2",
            deploymentId);

        Assert.True(outcome.IsComplete);

        // ---- the walk ---------------------------------------------------------------------------------
        var record = (await ledger.ReadAllAsync()).Single(r => r.DeploymentId == deploymentId);

        // DeploymentId → ReleaseId (+ environment) come out of the id, not out of a side table.
        Assert.Equal("rel-2ec4c364727bcb74", record.DeploymentId!.Release.Value);
        Assert.Equal(DeploymentEnvironmentId.TestEnv, record.DeploymentId.Environment);

        // → ArtifactId and → BuildId (the bundle the artifacts belong to).
        Assert.Equal(bundle.BundleId, record.BundleId);
        Assert.Equal(bundle.AllArtifacts.First().UnitId, record.Artifacts.First().UnitId);
        Assert.Equal(bundle.AllArtifacts.First().Digest, record.Artifacts.First().Digest);

        // → SourceCommit.
        Assert.Equal(bundle.Source.CommitSha, record.SourceCommitSha);

        // The hop to the WorkItem is carried by this LineageId in the governed workbook row.
        Assert.Equal("L-W9-2", record.LineageId);
    }

    /// <summary>
    /// <b>A legacy record stays legacy.</b> A line written before the identity transition point decodes with
    /// no <c>DeploymentId</c>, and nothing back-fills one — inventing an id for a record already written is
    /// the one thing an append-only ledger may not be made to do.
    /// </summary>
    [Fact]
    public void T7_ARecordWrittenWithoutADeploymentId_DecodesAsLegacy_AndIsNotBackFilled()
    {
        using var scratch = new TempDirectory("nexus-w94-t7-legacy-");
        var path = Path.Combine(scratch.Path, "lineage.jsonl");

        // The shape the governed W9.4 ledger actually holds: no DeploymentId member at all.
        File.WriteAllText(
            path,
            """
            {"LineageId":"L-W9-2","OccurredAt":"2026-09-25T12:57:16.4955411+00:00","Transition":"DeployToDev","BundleId":"nexus-2026.09.23-w93rel","FromState":"Registered","ToState":"DeployedDev","Environment":"ENV-DEV","SourceCommit":"2f6f93096067f7d13705d6259b9b982a0bb59f79","Artifacts":[{"UnitId":"marketsurvey.api","Digest":"sha256:0ab87652cb33dea1787e1255a67880abc288548daf52c4bccaf30a05b4af4a7e","SizeBytes":5434178}],"MigrationProvider":"ef-postgresql","MigrationSetDigest":"sha256:208fa794a7925db5f81c65dec9e574972f940d3eca908c869213dbd7a0f300a7","MigrationIds":["20260915074030_InitialSchema"],"AuthorizedBy":"w9.4-env-dev-deployment-driver","AuthorizationRole":"DeliveryTeam","Reason":"W9.4 first ENV-DEV deployment of the certified release.","ConfigurationKeys":["api"],"SecretReferences":[]}
            """ + Environment.NewLine);

        var record = new FileDeploymentLineageLog(scratch.Path).ReadAllAsync().GetAwaiter().GetResult().Single();

        Assert.Equal("L-W9-2", record.LineageId);
        Assert.Null(record.DeploymentId);
        Assert.Null(record.PreviousDeploymentId);
    }

    /// <summary>A deployment id is written to, and read back from, the governed ledger unchanged.</summary>
    [Fact]
    public async Task T7_ADeploymentIdSurvivesTheLedgerRoundTrip()
    {
        using var scratch = new TempDirectory("nexus-w94-t7-roundtrip-");
        var ledger = new FileDeploymentLineageLog(scratch.Path);
        var deploymentId = DeploymentId.For(
            ReleaseId.Parse("rel-2ec4c364727bcb74"), DeploymentEnvironmentId.TestEnv, 4);
        var previous = DeploymentId.For(
            ReleaseId.Parse("rel-2ec4c364727bcb74"), DeploymentEnvironmentId.TestEnv, 3);

        await new DeploymentPromoter(new FileArtifactRegistry(System.IO.Path.Combine(scratch.Path, "registry")), ledger).TransitionAsync(
            TestData.Request(TestData.Bundle(), DeploymentTransition.Register, PromotionState.Built),
            "L-W9-2",
            deploymentId,
            previous);

        var record = (await ledger.ReadAllAsync()).Single();

        Assert.Equal(deploymentId, record.DeploymentId);
        Assert.Equal(previous, record.PreviousDeploymentId);
    }

    /// <summary>
    /// The promoter refuses a <c>DeploymentId</c> that names a different environment from the one the
    /// transition targets. A deterministic id is easy to construct wrongly, and a wrong one is not
    /// detectable by reading it.
    /// </summary>
    [Fact]
    public async Task T7_ThePromoterRefusesADeploymentIdForADifferentEnvironment()
    {
        using var scratch = new TempDirectory("nexus-w94-t7-mismatch-");

        var wrongEnvironment = DeploymentId.For(
            ReleaseId.Parse("rel-2ec4c364727bcb74"), DeploymentEnvironmentId.ProdEnv, 1);

        await Assert.ThrowsAsync<ArgumentException>(() => new DeploymentPromoter(
                new FileArtifactRegistry(System.IO.Path.Combine(scratch.Path, "registry")),
                new FileDeploymentLineageLog(scratch.Path))
            .TransitionAsync(
                TestData.Request(TestData.Bundle(), DeploymentTransition.Register, PromotionState.Built),
                "L-W9-2",
                wrongEnvironment));
    }

    // =============================================================================================
    // TASK 1 / TASK 2 — immutable, attempt-scoped verification evidence
    // =============================================================================================

    private static IReadOnlyDictionary<string, object?> Facts(string marker)
        => new Dictionary<string, object?>(StringComparer.Ordinal) { ["marker"] = marker };

    /// <summary>
    /// The identity is addressable: the same deployment and attempt always resolve to the same path, and a
    /// different attempt to a different one. That is what makes "resolve the correct envelope" a
    /// computation rather than a lookup in someone's notes.
    /// </summary>
    [Fact]
    public void T1_TheEvidencePathIsAFunctionOfTheDeploymentIdAndTheAttempt()
    {
        var deployment = EvidenceDeployment();

        var first = DeploymentEvidenceTransaction.ForAttempt("C:/root", deployment, 1).EvidencePath;
        var second = DeploymentEvidenceTransaction.ForAttempt("C:/root", deployment, 2).EvidencePath;

        Assert.NotEqual(first, second);
        Assert.Contains("deployments", first, StringComparison.Ordinal);
        Assert.Contains(deployment.Value, first, StringComparison.Ordinal);
        Assert.EndsWith("DEV_VERIFICATION.json", first, StringComparison.Ordinal);

        // Deterministic: computed twice, identical.
        Assert.Equal(first, DeploymentEvidenceTransaction.ForAttempt("C:/root", deployment, 1).EvidencePath);
    }

    /// <summary>Same deployment, same attempt, same evidence ⇒ idempotent. Nothing is rewritten.</summary>
    [Fact]
    public async Task T2_TheSameAttemptWithTheSameEvidence_IsIdempotent()
    {
        using var scratch = new TempDirectory("nexus-w94-t2-same-");
        var transaction = DeploymentEvidenceTransaction.ForAttempt(scratch.Path, EvidenceDeployment(), 1);

        var first = await transaction.StageAsync(
            "rel-2ec4c364727bcb74", DeploymentEnvironmentId.DevEnv, EvidenceDeployment(), 1, "L-W9-7", Facts("same"));

        var second = await transaction.StageAsync(
            "rel-2ec4c364727bcb74", DeploymentEnvironmentId.DevEnv, EvidenceDeployment(), 1, "L-W9-7", Facts("same"));

        Assert.Equal(DeploymentEvidenceStaging.Created, first.Staging);
        Assert.Equal(DeploymentEvidenceStaging.Existing, second.Staging);

        // Not merely equal — NOT REWRITTEN. The staging timestamp is the evidence of that: a rewrite would
        // have a later one.
        Assert.Equal(first.Envelope.StagedAtUtc, second.Envelope.StagedAtUtc);
    }

    /// <summary>
    /// Same deployment, same attempt, <b>different</b> evidence ⇒ REFUSE. Two different accounts of one
    /// attempt is a contradiction, and the second does not get to replace the first — that is precisely the
    /// defect the environment-scoped path produced.
    /// </summary>
    [Fact]
    public async Task T2_TheSameAttemptWithDifferentEvidence_IsRefused()
    {
        using var scratch = new TempDirectory("nexus-w94-t2-diff-");
        var transaction = DeploymentEvidenceTransaction.ForAttempt(scratch.Path, EvidenceDeployment(), 1);

        await transaction.StageAsync(
            "rel-2ec4c364727bcb74", DeploymentEnvironmentId.DevEnv, EvidenceDeployment(), 1, "L-W9-7", Facts("first"));

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.StageAsync(
            "rel-2ec4c364727bcb74", DeploymentEnvironmentId.DevEnv, EvidenceDeployment(), 1, "L-W9-8", Facts("second")));

        Assert.Contains("immutable", refusal.Message, StringComparison.OrdinalIgnoreCase);

        // And the original is intact.
        Assert.Equal("L-W9-7", transaction.Read(transaction.EvidencePath).IntendedLineageId);
    }

    /// <summary>
    /// A new attempt writes a <b>new</b> envelope and leaves the earlier one byte-identical. This is the
    /// property whose absence destroyed the L-W9-4 and L-W9-5 envelopes.
    /// </summary>
    [Fact]
    public async Task T2_ANewAttempt_GetsItsOwnEnvelope_AndLeavesTheEarlierOneUntouched()
    {
        using var scratch = new TempDirectory("nexus-w94-t2-new-");
        var deployment = EvidenceDeployment();

        var attempt1 = DeploymentEvidenceTransaction.ForAttempt(scratch.Path, deployment, 1);
        var attempt2 = DeploymentEvidenceTransaction.ForAttempt(scratch.Path, deployment, 2);

        await attempt1.StageAsync(
            "rel-2ec4c364727bcb74", DeploymentEnvironmentId.DevEnv, deployment, 1, "L-W9-7", Facts("attempt one"));

        var before = File.ReadAllBytes(attempt1.EvidencePath);
        var beforeDigest = attempt1.Read(attempt1.EvidencePath).ComputeDigest();

        await attempt2.StageAsync(
            "rel-2ec4c364727bcb74", DeploymentEnvironmentId.DevEnv, deployment, 2, "L-W9-8", Facts("attempt two"));

        Assert.NotEqual(attempt1.EvidencePath, attempt2.EvidencePath);
        Assert.True(File.Exists(attempt1.EvidencePath));
        Assert.True(File.Exists(attempt2.EvidencePath));

        // Byte-identical, not merely still present.
        Assert.Equal(before, File.ReadAllBytes(attempt1.EvidencePath));
        Assert.Equal(beforeDigest, attempt1.Read(attempt1.EvidencePath).ComputeDigest());
        Assert.Equal(2, attempt2.Read(attempt2.EvidencePath).Attempt);
    }

    /// <summary>A sealed envelope is never resealed — the evidence of a committed transition is final.</summary>
    [Fact]
    public async Task T2_ACommittedEnvelope_IsNeverResealed()
    {
        using var scratch = new TempDirectory("nexus-w94-t2-sealed-");
        var transaction = DeploymentEvidenceTransaction.ForAttempt(scratch.Path, EvidenceDeployment(), 1);

        var staged = await transaction.StageAsync(
            "rel-2ec4c364727bcb74", DeploymentEnvironmentId.DevEnv, EvidenceDeployment(), 1, "L-W9-7", Facts("sealed"));

        await transaction.CommitAsync(staged, () => Task.FromResult("L-W9-7"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transaction.CommitAsync(staged, () => Task.FromResult("L-W9-8")));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A scratch directory that cannot be removed is not a test failure.
            }
        }
    }
}
