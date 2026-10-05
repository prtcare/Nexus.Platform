using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Nexus.Delivery.Release;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// The gates read the governed release plan — and nothing else.
///
/// <para>
/// <b>What went wrong that these tests exist to prevent.</b> The release/security gates consumed
/// <c>plan.CredentialRotationConfirmed</c>, <c>plan.ResolveReferenceProtection()</c> and
/// <c>plan.ReleaseRefServerSideProtectionVerified</c> — three members of the same untracked, hand-edited
/// JSON file the release run was configured from. The authority controlling a deployment and the
/// configuration of the tool that read it were the same artifact, with no writer, no schema, no validation
/// and no review.
/// </para>
///
/// <para>
/// <b>These tests exercise the driver's own mapping</b> (<c>Release.Program.BuildGateEvidence</c>), not a
/// re-expression of it. A suite that restated the mapping would pass forever while the driver read something
/// else, which is the failure mode this whole task exists to remove.
/// </para>
/// </summary>
public sealed class GovernedReleaseSecurityGateTests
{
    private static readonly ReleaseId Release = ReleaseId.Parse("rel-2ec4c364727bcb74");

    private static ReleaseRecord Record => ReleaseTestData.Record();

    /// <summary>
    /// The W9 state as the governed store holds it: rotation Confirmed with the Owner's decision, and the
    /// release reference protected by the Owner-approved, verified, DEV/TEST-scoped compensating control.
    /// </summary>
    private static ReleaseSecurityPlan GovernedPlan() => new()
    {
        SchemaVersion = ReleaseSecurityPlan.CurrentSchemaVersion,
        ReleaseId = Release,
        CredentialRotationStatus = CredentialRotationStatus.Confirmed,
        CredentialRotationDecisionReference = "OWNER-DECISION-C1",
        ReleaseReferenceProtectionMode = ReleaseReferenceProtectionMode.GovernedCompensatingControl,
        ReleaseRefServerSideProtectionVerified = false,
        CompensatingControlApproved = true,
        CompensatingControlVerified = true,
        AllowedEnvironmentScope = ReleaseReferenceProtectionScope.DevTest,
        HumanDecisionReference = "OWNER-DECISION-C2",
        HumanDecisionAuthority = DeploymentAuthorityRole.Owner,
        UpdatedAt = DateTimeOffset.Parse("2026-09-25T00:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
        UpdatedBy = "owner",
        Reason = "W9.4 migration of the C-1 and C-2 facts.",
        Version = 1
    };

    /// <summary>A tag assessment that is governed locally and, truthfully, unprotected remotely.</summary>
    private static ReleaseTagAssessment GouvernedAssessment()
        => ReleaseTagAssessment.Ineligible(
            new ReleaseTagDescriptor(
                "NEXUS/Platform",
                IReleaseTagPolicy.RefNameFor(Record),
                Exists: true,
                IsAnnotated: true,
                ReleaseRefProtectionStatus.Unprotected,
                TagObjectSha: new string('9', 40)),
            [ReleaseTagRefusalReason.ServerSideProtectionUnverified],
            ["No server-side ruleset is installed on this repository, and none is assumed."]);

    private static ReleaseGateEvidence Evidence(
        ReleaseRecord release,
        ReleaseSecurityPlan? governedPlan,
        SecretScanEvidence? scan,
        ReleaseTagAssessment? assessment)
        => global::Nexus.Delivery.Release.Program.BuildGateEvidence(
            release,
            storeVerification: null,
            scan ?? CleanScan(),
            assessment ?? GouvernedAssessment(),
            governedPlan,
            out _);

    private static SecretScanEvidence CleanScan()
        => new(
            SecretScanVerdict.Clean,
            ["NEXUS/Platform"],
            [],
            128,
            0,
            [],
            []);

    // =====================================================================================================
    // TASK 5 — the governed plan is the authority, and its absence fails closed
    // =====================================================================================================

    /// <summary>
    /// With the W9 governed plan recorded, certification reaches a full <c>Certified</c> verdict — the state
    /// TASK 7 requires before the lifecycle may advance.
    /// </summary>
    [Fact]
    public void WithTheGovernedPlanRecorded_TheReleaseCertifies_withNoOutstandingAction()
    {
        var evidence = Evidence(Record, GovernedPlan(), null, null);

        var certification = ReleaseCertificationGate.Evaluate(Record, evidence);

        Assert.Equal(ReleaseCertificationVerdict.Certified, certification.Verdict);
        Assert.True(certification.IsCertified);
        Assert.Empty(certification.OutstandingSecurityActions);

        // The two members the driver derived from the governed plan are the plan's own, not defaults.
        Assert.True(evidence.CredentialRotationConfirmed);
        Assert.Equal(ReleaseReferenceProtectionMode.GovernedCompensatingControl, evidence.ReleaseReferenceProtection.Mode);
        Assert.True(evidence.ReleaseReferenceProtection.CompensatingControlApproved);
        Assert.True(evidence.ReleaseReferenceProtection.CompensatingControlVerified);
    }

    /// <summary>
    /// <b>No governed plan is not a pass.</b> A brand-new release with nothing recorded routes to the pending
    /// security state, because the driver supplies <i>unobserved</i> for both actions rather than a default
    /// that happens to satisfy them.
    /// </summary>
    [Fact]
    public void WithNoGovernedPlan_TheReleaseRoutesToPendingSecurityAction()
    {
        var evidence = Evidence(Record, governedPlan: null, null, null);

        Assert.Null(evidence.CredentialRotationConfirmed);

        var certification = ReleaseCertificationGate.Evaluate(Record, evidence);

        Assert.Equal(ReleaseCertificationVerdict.CertifiedPendingSecurityAction, certification.Verdict);

        // IsCertified means "not refused"; IsDeployable is the strict verdict. Asserting the strict one is
        // the point: a pending security action is a certified release that may not be deployed, and a test
        // that checked only IsCertified would pass on exactly the state this asserts is not a pass.
        Assert.False(certification.IsDeployable);
        Assert.NotEmpty(certification.OutstandingSecurityActions);
    }

    /// <summary>
    /// <b>The driver cannot be handed a security fact.</b> There is no parameter for rotation or protection:
    /// the only way to reach <c>Certified</c> is to pass the record a governed writer produced. This asserts
    /// the shape of the seam rather than a behaviour, because the shape is the control — a driver with a
    /// <c>bool credentialRotationConfirmed</c> parameter would put the decision back in the caller's hands.
    /// </summary>
    [Fact]
    public void TheEvidenceSeam_AcceptsNoSecurityFactFromTheCaller()
    {
        var parameters = typeof(global::Nexus.Delivery.Release.Program)
            .GetMethod("BuildGateEvidence", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetParameters()
            .Select(p => p.ParameterType.Name)
            .ToList();

        Assert.Contains(nameof(ReleaseSecurityPlan), parameters);
        Assert.DoesNotContain("Boolean", parameters);
        Assert.DoesNotContain("ReleaseReferenceProtectionEvidence", parameters);
    }

    [Fact]
    public void EveryMemberOfTheGovernedPlan_ReachesTheGateUnchanged()
    {
        var plan = GovernedPlan();
        var protection = plan.ToProtectionEvidence();

        Assert.Equal(plan.ReleaseReferenceProtectionMode, protection.Mode);
        Assert.Equal(plan.ReleaseRefServerSideProtectionVerified, protection.ServerSideProtectionVerified);
        Assert.Equal(plan.CompensatingControlApproved, protection.CompensatingControlApproved);
        Assert.Equal(plan.CompensatingControlVerified, protection.CompensatingControlVerified);
        Assert.Equal(plan.AllowedEnvironmentScope, protection.AllowedScope);

        // And the two gates agree, per environment, because there is one judgement.
        Assert.True(protection.Judge(DeploymentEnvironmentId.DevEnv).IsSatisfied);
        Assert.True(protection.Judge(DeploymentEnvironmentId.TestEnv).IsSatisfied);
        Assert.False(protection.Judge(DeploymentEnvironmentId.ProdEnv).IsSatisfied);
    }

    // =====================================================================================================
    // TASK 7 — the transition, and a negative control for every condition it requires
    // =====================================================================================================

    [Fact]
    public void TheTransitionIsPermitted_WhenEveryConditionHolds()
    {
        var evidence = Evidence(Record, GovernedPlan(), null, null);

        var decision = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReadyForDevPendingSecurityAction,
            ReleaseTransition.DeclareReadyForDev,
            Record,
            evidence);

        Assert.True(decision.IsAllowed, string.Join(", ", decision.RefusalReasons));
        Assert.Equal(ReleaseLifecycleState.ReadyForDev, decision.ToState);
        Assert.Equal(ReleaseLifecycleState.ReadyForDevPendingSecurityAction, decision.FromState);
    }

    /// <summary>Condition 1: rotation must be Confirmed. Required and Unknown both refuse.</summary>
    [Theory]
    [InlineData(CredentialRotationStatus.Unknown)]
    [InlineData(CredentialRotationStatus.Required)]
    public void TheTransitionIsRefused_WhenRotationIsNotConfirmed(CredentialRotationStatus rotation)
    {
        var plan = GovernedPlan() with
        {
            CredentialRotationStatus = rotation,
            CredentialRotationDecisionReference = null
        };

        AssertRefused(plan, ReleaseRefusalReason.SecurityActionOutstanding);
    }

    /// <summary>Condition 2a: the C-2 control must be approved.</summary>
    [Fact]
    public void TheTransitionIsRefused_WhenTheCompensatingControlIsNotApproved()
    {
        AssertRefused(GovernedPlan() with { CompensatingControlApproved = false }, ReleaseRefusalReason.SecurityActionOutstanding);
    }

    /// <summary>Condition 2b: and verified — an approved control that was never exercised is a plan.</summary>
    [Fact]
    public void TheTransitionIsRefused_WhenTheCompensatingControlIsNotVerified()
    {
        AssertRefused(GovernedPlan() with { CompensatingControlVerified = false }, ReleaseRefusalReason.SecurityActionOutstanding);
    }

    /// <summary>
    /// Condition 3: the allowed scope must include DEV. The compensating control's scope is DEV/TEST
    /// <i>or nothing</i> — a record with no scope has not said where it applies, so it cannot stand in.
    /// </summary>
    [Fact]
    public void TheTransitionIsRefused_WhenNoEnvironmentScopeIsRecorded()
    {
        AssertRefused(GovernedPlan() with { AllowedEnvironmentScope = ReleaseReferenceProtectionScope.None }, ReleaseRefusalReason.SecurityActionOutstanding);
    }

    /// <summary>
    /// Condition 4: the release's technical evidence must be intact. A release whose scan is dirty, or whose
    /// bundle is incomplete, does not advance because its security actions were recorded.
    /// </summary>
    [Theory]
    [InlineData("scan")]
    [InlineData("ref")]
    public void TheTransitionIsRefused_WhenTheTechnicalEvidenceRegressed(string which)
    {
        var evidence = Evidence(
            Record,
            GovernedPlan(),
            which == "scan"
                ? new SecretScanEvidence(SecretScanVerdict.Findings, ["NEXUS/Platform"], [], 12, 0, ["src/appsettings.json:3"], [])
                : null,
            which == "ref"
                ? ReleaseTagAssessment.Ineligible(
                    null,
                    [ReleaseTagRefusalReason.TagAbsent],
                    ["No governed release reference exists."])
                : null);

        var decision = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReadyForDevPendingSecurityAction,
            ReleaseTransition.DeclareReadyForDev,
            Record,
            evidence);

        Assert.False(decision.IsAllowed);
    }

    /// <summary>
    /// <b>The transition cannot be taken from a state that precedes the security action.</b> If the widened
    /// predecessor set ever grew to include <c>ReleaseCertified</c> alone, the pending state would become
    /// optional and the deviation would have stopped being a deviation.
    /// </summary>
    [Fact]
    public void TheTransitionIsStillPermitted_FromCertified_ButNotFromEarlierStates()
    {
        var evidence = Evidence(Record, GovernedPlan(), null, null);

        Assert.True(ReleaseLifecycleMachine
            .Decide(ReleaseLifecycleState.ReleaseCertified, ReleaseTransition.DeclareReadyForDev, Record, evidence)
            .IsAllowed);

        foreach (var state in new[] { ReleaseLifecycleState.ReleaseDraft, ReleaseLifecycleState.Refused, ReleaseLifecycleState.Withdrawn })
        {
            Assert.False(ReleaseLifecycleMachine
                .Decide(state, ReleaseTransition.DeclareReadyForDev, Record, evidence)
                .IsAllowed);
        }
    }

    private static void AssertRefused(ReleaseSecurityPlan plan, ReleaseRefusalReason expected)
    {
        var evidence = Evidence(Record, plan, null, null);

        var decision = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReadyForDevPendingSecurityAction,
            ReleaseTransition.DeclareReadyForDev,
            Record,
            evidence);

        Assert.False(decision.IsAllowed);
        Assert.True(decision.RefusedBecause(expected), $"expected {expected}; got [{string.Join(", ", decision.RefusalReasons)}]");

        // Companion: the unmodified plan DOES advance, so the refusal above is attributable to the one member
        // the test broke rather than to a builder that was never sufficient.
        var control = ReleaseLifecycleMachine.Decide(
            ReleaseLifecycleState.ReadyForDevPendingSecurityAction,
            ReleaseTransition.DeclareReadyForDev,
            Record,
            Evidence(Record, GovernedPlan(), null, null));

        Assert.True(control.IsAllowed);
    }

    // =====================================================================================================
    // TASK 6 — the codec false reading, as a regression that names itself
    // =====================================================================================================

    /// <summary>
    /// <b>INSTRUMENTATION_CODEC_FALSE_READING.</b> The W9.4 forensic finding, as a test: an unknown migration
    /// compatibility decodes as <i>unknown</i>, not as <c>Match</c>. When it decoded as <c>Match</c>, a
    /// registered release whose compatibility was never recorded recomputed to a different digest than the one
    /// registered, and the registry reported REVIEWED, INTACT BYTES as a corruption finding. Nothing was
    /// tampered with; the instrument lied.
    ///
    /// <para>
    /// <b>Why this is asserted through the DECODE path.</b> <c>RegisterAsync</c> compares the record the caller
    /// already holds in memory, so it cannot see this defect at all. <c>VerifyAsync</c> decodes the stored
    /// record and recomputes its digest, which is the only path on which the false reading is visible — and
    /// therefore the only path on which this regression means anything.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CodecFalseReading_AnUnrecordedCompatibility_SurvivesADecodeWithItsDigestIntact()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexus-codec-false-reading", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var release = ReleaseTestData.Record(
                migrations: MigrationAssessment.Required(
                    new MigrationMetadata("Npgsql.EntityFrameworkCore.PostgreSQL", ["20260901_Initial"]),
                    "20260801",
                    "20260901",
                    compatibility: null,
                    backupRequired: true,
                    MigrationReversibility.ForwardFixOnly,
                    "Five PostgreSQL migrations are recorded; no target was inspected, so compatibility is not established."));

            Assert.Null(release.Migrations.Compatibility);

            var registry = new FileReleaseRegistry(root);

            Assert.True((await registry.RegisterAsync(release)).IsAccepted);

            var verification = await registry.VerifyAsync(release.ReleaseId);

            Assert.True(verification.IsIntact, verification.Detail);
            Assert.True(verification.RegistryContentIntact);
            Assert.Equal(release.ComputeRecordDigest(), verification.RecordedDigest);
            Assert.Equal(release.ComputeRecordDigest(), verification.RecomputedDigest);

            // And the decoded record itself still says "unknown" rather than the nearest convenient value.
            var decoded = await registry.TryOpenAsync(release.ReleaseId);

            Assert.NotNull(decoded);
            Assert.Null(decoded!.Migrations.Compatibility);
            Assert.Equal(release.ComputeRecordDigest(), decoded.ComputeRecordDigest());
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
