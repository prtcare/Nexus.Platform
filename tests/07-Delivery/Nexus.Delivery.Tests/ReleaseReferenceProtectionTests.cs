using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// C-2's model, at the contract level: two mechanisms, one judgement, and a boundary that is enforced rather
/// than documented.
///
/// <para>
/// <b>What went wrong that these tests exist to prevent.</b> The compensating control was previously recorded
/// as <c>ReleaseRefServerSideProtectionVerified = true</c>. On this estate that is simply false — no remote
/// ruleset exists and none can be installed on these repositories — so the single boolean that DEV readiness,
/// ENV-TEST promotion and ENV-PROD promotion all consulted said the same thing in all three places. The
/// compensating control's whole justification is that it is a <i>DEV/TEST</i> substitute; a record that cannot
/// express that scope cannot enforce it.
/// </para>
///
/// <para>
/// <b>The load-bearing tests here are the refusals</b> — above all that a compensating control does not carry
/// an ENV-PROD promotion, and that it never reports itself as the control it replaces.
/// </para>
/// </summary>
public sealed class ReleaseReferenceProtectionTests
{
    private static readonly DeploymentEnvironmentId[] Ratified =
        [DeploymentEnvironmentId.DevEnv, DeploymentEnvironmentId.TestEnv, DeploymentEnvironmentId.ProdEnv];

    /// <summary>Nothing recorded is not a pass, anywhere — and it is reported as unobserved, not as a failure.</summary>
    [Fact]
    public void AnUnrecordedMechanism_IsNotSatisfiedAnywhere_AndIsClassifiedAsUnobserved()
    {
        var unrecorded = ReleaseReferenceProtectionEvidence.Unrecorded;

        Assert.Equal(ReleaseReferenceProtectionMode.None, unrecorded.Mode);

        foreach (var environment in Ratified)
        {
            var judgement = unrecorded.Judge(environment);

            Assert.False(judgement.IsSatisfied, $"{environment} must not be satisfied by an unrecorded mechanism.");
            Assert.True(judgement.IsUnobserved, "A missing observation is NOT_RUN, never a pass.");
            Assert.StartsWith(ReleaseReferenceProtectionJudgement.UnrecordedCode, judgement.Detail, StringComparison.Ordinal);
        }

        // And the default member state is the same thing, so a caller who never mentions protection gets the
        // refusing answer rather than a silent exemption.
        Assert.Equal(ReleaseReferenceProtectionMode.None, new ReleaseGateEvidence().ReleaseReferenceProtection.Mode);
        Assert.Equal(ReleaseReferenceProtectionMode.None, new DeploymentGateEvidence().ReleaseReferenceProtection.Mode);
    }

    /// <summary>Server-side protection is not environment-scoped: a ruleset is a property of the repository.</summary>
    [Theory]
    [InlineData(DeploymentEnvironmentId.Dev)]
    [InlineData(DeploymentEnvironmentId.Test)]
    [InlineData(DeploymentEnvironmentId.Prod)]
    public void ServerSideProtection_SatisfiesEveryRatifiedEnvironment(string environment)
    {
        var judgement = ReleaseReferenceProtectionEvidence.ServerSide().Judge(DeploymentEnvironmentId.Parse(environment));

        Assert.True(judgement.IsSatisfied);
        Assert.False(judgement.IsUnobserved);
    }

    [Fact]
    public void ServerSideProtection_IsNotSatisfiedUntilAnAuthorizedCheckConfirmsIt()
    {
        var absent = ReleaseReferenceProtectionEvidence.ServerSide(verified: false).Judge(DeploymentEnvironmentId.DevEnv);

        Assert.False(absent.IsSatisfied);
        Assert.False(absent.IsUnobserved);
        Assert.StartsWith(ReleaseReferenceProtectionJudgement.OutstandingCode, absent.Detail, StringComparison.Ordinal);

        var unobserved = ReleaseReferenceProtectionEvidence.ServerSide(verified: null).Judge(DeploymentEnvironmentId.DevEnv);

        Assert.False(unobserved.IsSatisfied);
        Assert.True(unobserved.IsUnobserved);
    }

    /// <summary>
    /// <b>The C-2 boundary, enforced.</b> The Owner approved the compensating control for ENV-DEV and ENV-TEST.
    /// It is not a smaller amount of the same protection in ENV-PROD; it is not available there at all.
    /// </summary>
    [Theory]
    [InlineData(DeploymentEnvironmentId.Dev, true)]
    [InlineData(DeploymentEnvironmentId.Test, true)]
    [InlineData(DeploymentEnvironmentId.Prod, false)]
    public void TheCompensatingControl_IsSatisfiedWhereItIsScoped_AndRefusedWhereItIsNot(string environment, bool satisfied)
    {
        var parsed = DeploymentEnvironmentId.Parse(environment);

        var judgement = ReleaseReferenceProtectionEvidence.CompensatingControl().Judge(parsed);

        Assert.Equal(satisfied, judgement.IsSatisfied);

        // The refusal in ENV-PROD is a DECISION, not a missing observation: the evidence was complete and the
        // answer is still no. Classifying it as unobserved would put it in the evidence-incomplete bucket and
        // send someone to gather evidence that would not change the answer.
        Assert.False(judgement.IsUnobserved);

        if (!satisfied)
        {
            Assert.StartsWith(ReleaseReferenceProtectionJudgement.OutstandingCode, judgement.Detail, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// And the refusal says why in the deviation's own terms, so it is not read as a defect.
    /// </summary>
    [Fact]
    public void TheCompensatingControl_RefusalNamesTheScope_NotADefect()
    {
        var judgement = ReleaseReferenceProtectionEvidence.CompensatingControl().Judge(DeploymentEnvironmentId.ProdEnv);

        Assert.Contains(DeploymentEnvironmentId.Dev, judgement.Detail, StringComparison.Ordinal);
        Assert.Contains(DeploymentEnvironmentId.Test, judgement.Detail, StringComparison.Ordinal);
        Assert.Contains("boundary", judgement.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheCompensatingControl_RequiresApproval_AndVerification()
    {
        var notApproved = ReleaseReferenceProtectionEvidence.CompensatingControl(approved: false)
            .Judge(DeploymentEnvironmentId.DevEnv);

        Assert.False(notApproved.IsSatisfied);
        Assert.False(notApproved.IsUnobserved);

        var notVerified = ReleaseReferenceProtectionEvidence.CompensatingControl(verified: false)
            .Judge(DeploymentEnvironmentId.DevEnv);

        Assert.False(notVerified.IsSatisfied);
        Assert.False(notVerified.IsUnobserved);

        // Approval is the Owner's act and verification is the control's; an unrecorded one of either is
        // NOT_RUN rather than a failure.
        Assert.True(ReleaseReferenceProtectionEvidence.CompensatingControl(approved: null)
            .Judge(DeploymentEnvironmentId.DevEnv).IsUnobserved);
        Assert.True(ReleaseReferenceProtectionEvidence.CompensatingControl(verified: null)
            .Judge(DeploymentEnvironmentId.DevEnv).IsUnobserved);
    }

    /// <summary>
    /// A compensating control recorded as covering every environment is <b>refused rather than narrowed</b>.
    /// Quietly treating it as DEV/TEST would leave the record asserting one thing and the behaviour doing
    /// another, which is the failure mode this whole model replaced.
    /// </summary>
    [Theory]
    [InlineData(ReleaseReferenceProtectionScope.AllRatifiedEnvironments)]
    [InlineData(ReleaseReferenceProtectionScope.None)]
    public void ACompensatingControlWithAnUnapprovedScope_IsRefused_EvenInDev(ReleaseReferenceProtectionScope scope)
    {
        var judgement = ReleaseReferenceProtectionEvidence
            .CompensatingControl(scope: scope)
            .Judge(DeploymentEnvironmentId.DevEnv);

        Assert.False(judgement.IsSatisfied);
        Assert.False(judgement.IsUnobserved);
        Assert.Contains("scope", judgement.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <b>The overstatement control.</b> A satisfied compensating control must not describe itself as
    /// server-side protection. This is the sentence that was wrong before: the deviation is a substitute, and
    /// a substituted control that reported itself as the original is exactly what the deviation forbids.
    /// </summary>
    [Fact]
    public void TheCompensatingControl_NeverReportsItselfAsServerSideProtection()
    {
        var judgement = ReleaseReferenceProtectionEvidence.CompensatingControl().Judge(DeploymentEnvironmentId.DevEnv);

        Assert.True(judgement.IsSatisfied);
        Assert.Contains("C-2 compensating control", judgement.Detail, StringComparison.Ordinal);
        Assert.Contains("NOT claimed", judgement.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("ruleset protects", judgement.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two mechanisms are not interchangeable in either direction — asserted as a pair, so a future change
    /// that made one satisfy the other's condition fails a test rather than passing quietly.
    /// </summary>
    [Fact]
    public void TheTwoMechanisms_AreNotInterchangeable()
    {
        // A recorded compensating control does not stand in for a ruleset at the edge of its scope.
        Assert.False(ReleaseReferenceProtectionEvidence.CompensatingControl()
            .Judge(DeploymentEnvironmentId.ProdEnv).IsSatisfied);

        // And a ruleset that no authorized check has confirmed does not stand in for anything, even though
        // the mechanism itself is the stronger one.
        Assert.False(ReleaseReferenceProtectionEvidence.ServerSide(verified: false)
            .Judge(DeploymentEnvironmentId.DevEnv).IsSatisfied);

        Assert.NotEqual(
            ReleaseReferenceProtectionEvidence.ServerSide().Mode,
            ReleaseReferenceProtectionEvidence.CompensatingControl().Mode);
    }

    /// <summary>
    /// End to end on the certification path: this release, certified with the deviation's evidence, records
    /// the substitution on its own certification rather than leaving a reader to infer it.
    /// </summary>
    [Fact]
    public void ACertifiedRelease_RecordsHowItsReferenceIsProtected()
    {
        var decision = ReleaseCertificationGate.Evaluate(
            ReleaseTestData.Record(),
            ReleaseTestData.FullyClearedEvidence());

        Assert.Equal(ReleaseCertificationVerdict.Certified, decision.Verdict);
        Assert.True(decision.IsDeployable);

        var recorded = string.Join("\n", decision.Detail);
        Assert.Contains("C-2 compensating control", recorded, StringComparison.Ordinal);
        Assert.Contains("NOT claimed", recorded, StringComparison.Ordinal);
    }

    /// <summary>
    /// The <see cref="ReleaseCertificationVerdict"/> detail change is additive: a caller that supplies nothing
    /// still gets the bare marker, so the existing verdict contract is unchanged for every other path.
    /// </summary>
    [Fact]
    public void ACertificationWithNoDetailSupplied_KeepsTheBareMarker()
    {
        var decision = ReleaseCertificationDecision.Certified(ReleaseTestData.Record().ReleaseId);

        Assert.Equal(["RELEASE_CERTIFIED"], decision.Detail);
    }

    /// <summary>
    /// The two summary members on the evidence bag are derived from the same judgement rather than
    /// re-deciding it, and they keep the outstanding/unobserved distinction the lifecycle machine relies on.
    /// </summary>
    [Fact]
    public void TheEvidenceSummaryMembers_FollowTheJudgement()
    {
        var cleared = ReleaseTestData.FullyClearedEvidence();
        Assert.False(cleared.HasOutstandingSecurityAction);
        Assert.False(cleared.HasUnobservedSecurityAction);

        var outstanding = ReleaseTestData.ObservedEvidence(
            credentialRotationConfirmed: true,
            referenceProtection: ReleaseReferenceProtectionEvidence.CompensatingControl(verified: false));
        Assert.True(outstanding.HasOutstandingSecurityAction);
        Assert.False(outstanding.HasUnobservedSecurityAction);

        var unrecorded = ReleaseTestData.ObservedEvidence(
            credentialRotationConfirmed: true,
            referenceProtection: ReleaseReferenceProtectionEvidence.Unrecorded);
        Assert.False(unrecorded.HasOutstandingSecurityAction);
        Assert.True(unrecorded.HasUnobservedSecurityAction);

        // Rotation outstanding alone is enough, which is C-1's half of the same summary.
        Assert.True(ReleaseTestData.ObservedEvidence(credentialRotationConfirmed: false).HasOutstandingSecurityAction);
    }
}
