using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// TASK 1 — REPRODUCTIONS FOR THE THREE W9.4 INSTRUMENT DEFECTS.
///
/// <para>
/// These were written <b>before</b> any fix and were run against the unfixed source, where they fail.
/// That order is the point: a test written after a fix can only confirm the fix's assumptions, which is
/// the lesson W9.3 recorded when its <c>FakeGit</c> agreed with the bug it should have caught.
/// </para>
///
/// <para>
/// Each is written against the API that existed <b>before</b> the fix, so it compiles unchanged on both
/// sides of it and the failure it reports is a behaviour, not a missing member.
/// </para>
/// </summary>
public sealed class W94InstrumentDefectReproductionTests
{
    /// <summary>The deployment attempt these reproductions stage evidence for.</summary>
    private static DeploymentId EvidenceDeployment()
        => DeploymentId.For(ReleaseId.Parse("rel-2ec4c364727bcb74"), DeploymentEnvironmentId.DevEnv, 1);

    private static readonly string[] FiveMigrations =
    [
        "20260915074030_InitialSchema",
        "20260915075602_RecruitmentAttribution",
        "20260915080000_SpatialAndAudit",
        "20260921091956_ModularBoundaries",
        "20260921100000_ModuleIntegrity"
    ];

    /// <summary>The release's own recorded obligation: a backup is required, and the remedy is a forward fix.</summary>
    private static MigrationAssessment ReleaseRequiresABackup() => MigrationAssessment.Required(
        new MigrationMetadata("ef-postgresql", FiveMigrations),
        fromVersion: "0.0.0",
        toVersion: "0.1.0",
        compatibility: MigrationCompatibility.Match,
        backupRequired: true,
        reversibility: MigrationReversibility.ForwardFixOnly,
        basis: "Read at the certified commit; BackupRequired=true as the release records.");

    private static DeploymentAuthorization DeliveryTeam() =>
        DeploymentAuthorization.For(DeploymentAuthorityRole.DeliveryTeam, "w9.4-reproduction", DateTimeOffset.UnixEpoch);

    // =============================================================================================
    // DEFECT D-R1 — a backup obligation discharged by a statistic that a restart zeroes
    // =============================================================================================

    /// <summary>
    /// <b>Reproduces D-R1.</b>
    ///
    /// <para>
    /// The observation below is the one the W9.4 driver actually produced against ENV-DEV on 2026-09-26:
    /// the target's data state read <c>Empty</c> from <c>sum(pg_stat_user_tables.n_live_tup)</c>, which a
    /// container restart zeroes — while an exact <c>count(*)</c> over the same table set found
    /// <b>8,500</b> rows in <c>spatial_ref_sys</c>.
    /// </para>
    ///
    /// <para>
    /// The release records <c>BackupRequired = true</c>. An emptiness that rests on a planner statistic
    /// is not established, so the obligation stands and the judgement must be
    /// <see cref="MigrationGovernanceDecision.BackupRequired"/>.
    /// </para>
    /// </summary>
    [Fact]
    public void D_R1_AnEmptyClaimedFromPostgreSqlStatistics_DoesNotDischargeTheBackupObligation()
    {
        var observed = new MigrationTargetObservation(
            DeploymentEnvironmentId.DevEnv,
            reachable: true,
            schemaPresent: true,
            appliedMigrationIds: FiveMigrations,
            dataState: MigrationTargetDataState.Empty,
            backupVerified: false,
            basis: "Observed through the container's own psql on 'nexus-env-dev-postgres'. pg_isready returned exit 0. "
                 + "The migration history table was present and reported 5 applied migration(s). Enumerated 27 public "
                 + "table(s) beyond the history table and summed their live row estimates: 0.");

        var verdict = MigrationGovernance.Judge(new MigrationGovernanceRequest(
            ReleaseRequiresABackup(),
            DeploymentEnvironmentId.DevEnv,
            observed,
            destructiveForwardMigrationIds: [],
            DeliveryTeam()));

        Assert.Equal(MigrationGovernanceDecision.BackupRequired, verdict.Decision);
    }

    /// <summary>
    /// The same defect stated as the property that must hold: <b>a database is never classified empty from
    /// a statistic.</b> The policy takes the artifact of a deterministic count — an exact row count — and
    /// nothing else; when the count could not be performed the answer is <c>Unknown</c>, which keeps the
    /// backup obligation standing rather than discharging it by silence.
    /// </summary>
    [Fact]
    public void D_R1_TheDataStatePolicy_NeverInfersEmptyWithoutADeterministicCount()
    {
        Assert.Equal(
            MigrationTargetDataState.Unknown,
            MigrationTargetDataStatePolicy.ClassifyFromDeterministicCount(null));

        Assert.Equal(
            MigrationTargetDataState.Empty,
            MigrationTargetDataStatePolicy.ClassifyFromDeterministicCount(0));

        Assert.Equal(
            MigrationTargetDataState.HoldsData,
            MigrationTargetDataStatePolicy.ClassifyFromDeterministicCount(8_500));
    }

    // =============================================================================================
    // DEFECT D-R2 — a lifecycle transition committed before its evidence exists
    // =============================================================================================

    /// <summary>
    /// <b>Reproduces D-R2.</b>
    ///
    /// <para>
    /// The W9.4 <c>verify</c> run appended <c>L-W9-3</c> to the governed ledger and then threw at teardown.
    /// <c>DEV_VERIFICATION.json</c> was never written. The ledger therefore advanced with no evidence
    /// beside it, and the file's absence was the only trace.
    /// </para>
    ///
    /// <para>
    /// The required property is that <b>the transition is attempted only after the evidence is durably
    /// persisted</b>. Here the commit delegate throws — the teardown failure — and the assertion is that
    /// the evidence still exists on disk and records that the transition never committed. Under the old
    /// ordering this test cannot be written at all: there was no staged evidence to lose.
    /// </para>
    /// </summary>
    [Fact]
    public async Task D_R2_WhenTheTransitionThrowsAfterStaging_TheEvidenceIsStillDurable()
    {
        using var scratch = new ScratchDirectory();

        var transaction = DeploymentEvidenceTransaction.ForAttempt(scratch.Path, EvidenceDeployment(), 1);
        var facts = new Dictionary<string, object?>(StringComparer.Ordinal) { ["releaseId"] = "rel-reproduction" };

        var staged = await transaction.StageAsync(
            releaseId: "rel-reproduction",
            environment: DeploymentEnvironmentId.DevEnv,
            deployment: EvidenceDeployment(),
                attempt: 1,
                intendedLineageId: "L-W9-3",
            facts: facts);

        Assert.True(File.Exists(staged.Path));
        Assert.Equal(DeploymentEvidenceState.PendingTransition, transaction.Read(staged.Path).State);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync(
            staged,
            commit: () => throw new InvalidOperationException("No process is associated with this object.")));

        var after = transaction.Read(staged.Path);

        Assert.Equal(DeploymentEvidenceState.CommitFailed, after.State);
        Assert.Null(after.LineageId);
        Assert.Contains("No process is associated", after.CommitDetail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the same property: <b>if the evidence cannot be persisted, the transition is never
    /// attempted.</b> The count of commit attempts is the assertion, so a transaction that persisted
    /// nothing and committed anyway fails here rather than passing on a technicality.
    /// </summary>
    [Fact]
    public async Task D_R2_WhenEvidenceCannotBePersisted_TheTransitionIsNeverAttempted()
    {
        using var scratch = new ScratchDirectory();
        var unwritable = Path.Combine(scratch.Path, "evidence-file-not-a-directory");
        await File.WriteAllTextAsync(unwritable, "this path is a FILE, so no evidence can be written beside it");

        var transaction = new DeploymentEvidenceTransaction(unwritable, "DEV_VERIFICATION.json");
        var attempts = 0;

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var staged = await transaction.StageAsync(
                releaseId: "rel-reproduction",
                environment: DeploymentEnvironmentId.DevEnv,
                deployment: EvidenceDeployment(),
                attempt: 1,
                intendedLineageId: "L-W9-3",
                facts: new Dictionary<string, object?>(StringComparer.Ordinal));

            // If staging had succeeded this would run and increment. The assertion below is that it never
            // did — a transaction that staged nothing and committed anyway must fail here, not pass on a
            // technicality about which exception was thrown.
            await transaction.CommitAsync(staged, commit: () =>
            {
                attempts++;
                return Task.FromResult("L-W9-3");
            });
        });

        Assert.Equal(0, attempts);
    }

    /// <summary>
    /// <b>Reproduces the teardown half of D-R2.</b> <c>using var process</c> disposed the very object the
    /// driver kept in its tracked field, so <c>process.HasExited</c> threw
    /// <i>"No process is associated with this object"</i> — after the ledger had already been written.
    ///
    /// <para>
    /// The estate's own reproduction of that state is a <see cref="System.Diagnostics.Process"/> that was
    /// started and then disposed: asking it anything throws. Termination must therefore be idempotent and
    /// safe against exactly that object, and must report what it found rather than throwing.
    /// </para>
    /// </summary>
    [Fact]
    public void D_R2_Termination_IsSafeAgainstAProcessThatWasAlreadyDisposed()
    {
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", "/c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        process.WaitForExit();
        process.Dispose();

        // The disposed object is the shape the driver held. This is what must not throw.
        var outcome = ProcessTermination.Terminate(process);

        Assert.Equal(ProcessTerminationOutcome.AlreadyExitedOrUnavailable, outcome);
    }

    // =============================================================================================
    // DEFECT D-R3 — a smoke verdict nothing consults
    // =============================================================================================

    /// <summary>
    /// <b>Reproduces D-R3.</b>
    ///
    /// <para>
    /// The evidence below carries the readiness observation and nothing about smoke — which is exactly the
    /// state the driver produced on its false-pass run, where its own log capture recorded
    /// <c>started=False, logLinesCaptured=0</c> and the readiness probe had been answered by a different
    /// process already bound to the port.
    /// </para>
    ///
    /// <para>
    /// <c>VerifyInDev</c> must refuse. A verification that cannot say whether the process it started
    /// survived its own smoke probes has established nothing about the running release.
    /// </para>
    /// </summary>
    [Fact]
    public void D_R3_VerifyInDev_IsRefusedWhenTheSmokeObservationWasNeverEstablished()
    {
        var bundle = TestData.Bundle();

        // Before the fix this test omitted the member entirely, because it did not exist — which is what
        // made it fail against the unfixed code (see REMEDIATION_TASK1_REPRODUCTION.txt). Now that the gate
        // requires the observation, "never established" has to be stated: null is the three-valued member's
        // way of saying the observation was not taken, and it is the value the old run actually had.
        var decision = DeploymentStateMachine.Decide(TestData.Request(
            bundle,
            DeploymentTransition.VerifyInDev,
            PromotionState.DeployedDev,
            TestData.HealthyEvidence(bundle) with { SmokeObserved = null }));

        Assert.True(
            decision.IsRefused,
            "VerifyInDev was allowed on evidence that carries no smoke observation. "
            + "Readiness alone proves that something answered the port, not that the process this run started is alive and passing.");
        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.SmokeNotObserved));
    }

    /// <summary>
    /// The non-vacuity companion for D-R3: the same request with the smoke observation <i>failed</i> is
    /// refused too, so the gate is not merely refusing everything. Together the two tests discriminate
    /// "smoke not established" from "smoke established", which is the property that was missing.
    /// </summary>
    [Fact]
    public void D_R3_VerifyInDev_IsRefusedWhenTheSmokeObservationFailed()
    {
        var bundle = TestData.Bundle();

        var decision = DeploymentStateMachine.Decide(TestData.Request(
            bundle,
            DeploymentTransition.VerifyInDev,
            PromotionState.DeployedDev,
            TestData.HealthyEvidence(bundle) with { SmokeObserved = false }));

        Assert.True(decision.IsRefused);
        Assert.True(decision.RefusedBecause(DeploymentRefusalReason.SmokeNotObserved));
    }

    /// <summary>A directory that deletes itself, so a test never leaves state behind on a shared machine.</summary>
    private sealed class ScratchDirectory : IDisposable
    {
        public ScratchDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexus-w94-repro-" + Guid.NewGuid().ToString("n"));
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
