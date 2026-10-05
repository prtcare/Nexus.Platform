using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// The governed release plan's model and its rules, at the contract level.
///
/// <para>
/// <b>What went wrong that these tests exist to prevent.</b> C-1 and C-2 lived in an untracked, hand-edited
/// JSON file — <c>FIRST_RELEASE_PLAN.json</c> — with no writer, no schema, no validation and no review path.
/// That file could express <c>releaseRefServerSideProtectionVerified: false</c> next to
/// <c>credentialRotationConfirmed: true</c> and nothing anywhere checked the two against each other or
/// against the mechanism the release actually relied on. The Owner ruled that unacceptable as the authority
/// controlling a deployment.
/// </para>
///
/// <para>
/// <b>The load-bearing tests here are the refusals and the scopes.</b> Above all: this estate's truth
/// (<c>GovernedCompensatingControl</c>, approved, verified, DEV/TEST) must be ready for ENV-DEV and ENV-TEST
/// and must NOT be ready for ENV-PROD — and it must never be representable as a claim that a remote ruleset
/// was observed, because no ruleset exists on these repositories.
/// </para>
/// </summary>
public sealed class ReleaseSecurityPlanTests
{
    private static readonly ReleaseId Release = ReleaseId.Parse("rel-2ec4c364727bcb74");

    private const string RotationDecision = "OWNER-DECISION-C1-ROTATION-CONFIRMED-2026-09-24";
    private const string CompensatingDecision = "OWNER-DECISION-C2-DEV-TEST-COMPENSATING-CONTROL";

    /// <summary>
    /// The W9 state, recorded truthfully: rotation Confirmed by the Owner, and the release reference's
    /// protection established by the Owner-approved C-2 compensating control scoped to DEV/TEST.
    /// </summary>
    private static ReleaseSecurityPlan W9State() => new()
    {
        SchemaVersion = ReleaseSecurityPlan.CurrentSchemaVersion,
        ReleaseId = Release,
        CredentialRotationStatus = CredentialRotationStatus.Confirmed,
        CredentialRotationDecisionReference = RotationDecision,
        ReleaseReferenceProtectionMode = ReleaseReferenceProtectionMode.GovernedCompensatingControl,
        ReleaseRefServerSideProtectionVerified = false,
        CompensatingControlApproved = true,
        CompensatingControlVerified = true,
        AllowedEnvironmentScope = ReleaseReferenceProtectionScope.DevTest,
        HumanDecisionReference = CompensatingDecision,
        HumanDecisionAuthority = DeploymentAuthorityRole.Owner,
        UpdatedAt = DateTimeOffset.Parse("2026-09-25T00:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
        UpdatedBy = "owner",
        Reason = "W9.4 migration of the C-1 and C-2 facts out of the untracked release-run plan.",
        Version = 1
    };

    // =====================================================================================================
    // The W9 state is representable, and means what it says
    // =====================================================================================================

    [Fact]
    public void TheW9State_IsWellFormed()
    {
        var verdict = ReleaseSecurityPlanValidation.Validate(W9State());

        Assert.True(verdict.IsValid, string.Join(" | ", verdict.Detail));
        Assert.Empty(verdict.RefusalReasons);
    }

    /// <summary>
    /// <b>The truth must not overstate itself.</b> The compensating control is a substitute for a control this
    /// estate cannot install, so the record must say so — and the moment it claimed server-side verification
    /// it would be a false statement about the remote's settings rather than a recorded deviation.
    /// </summary>
    [Fact]
    public void TheW9State_DoesNotAndCannot_ClaimServerSideProtection()
    {
        var plan = W9State();

        Assert.False(plan.ReleaseRefServerSideProtectionVerified);
        Assert.Equal(ReleaseReferenceProtectionMode.GovernedCompensatingControl, plan.ReleaseReferenceProtectionMode);
        Assert.NotEqual(ReleaseReferenceProtectionMode.ServerSideProtected, plan.ReleaseReferenceProtectionMode);

        // And if someone records both at once, the record is refused rather than read as the stronger claim.
        var both = ReleaseSecurityPlanValidation.Validate(plan with { ReleaseRefServerSideProtectionVerified = true });

        Assert.False(both.IsValid);
        Assert.True(both.RefusedBecause(ReleasePlanRefusalReason.CompensatingControlClaimsServerSideProtection));
    }

    /// <summary>The scope is the whole justification of the deviation, so it is asserted per environment.</summary>
    [Fact]
    public void TheW9State_IsReadyForDevAndTest_AndNotForProd()
    {
        var plan = W9State();

        foreach (var environment in new[] { DeploymentEnvironmentId.DevEnv, DeploymentEnvironmentId.TestEnv })
        {
            var readiness = ReleaseSecurityPlanValidation.AssessReadiness(plan, environment);

            Assert.True(readiness.IsReady, $"{environment} should be permitted: {string.Join(" | ", readiness.Detail)}");
        }

        var prod = ReleaseSecurityPlanValidation.AssessReadiness(plan, DeploymentEnvironmentId.ProdEnv);

        Assert.False(prod.IsReady);
        Assert.NotEmpty(prod.RefusalReasons);
    }

    /// <summary>
    /// A plan that records no mechanism is an honest unfinished state and must be <b>storable</b> — but it is
    /// never <b>ready</b>, anywhere. The two are split deliberately: a store that refused to record an
    /// unfinished state would make the unfinished state invisible, and a readiness check that passed it would
    /// make it a pass.
    /// </summary>
    [Fact]
    public void AnUnrecordedMechanism_IsStorable_AndReadyNowhere()
    {
        var unrecorded = W9State() with
        {
            CredentialRotationStatus = CredentialRotationStatus.Unknown,
            CredentialRotationDecisionReference = null,
            ReleaseReferenceProtectionMode = ReleaseReferenceProtectionMode.None,
            CompensatingControlApproved = false,
            CompensatingControlVerified = false,
            AllowedEnvironmentScope = ReleaseReferenceProtectionScope.None,
            HumanDecisionReference = null
        };

        Assert.True(ReleaseSecurityPlanValidation.Validate(unrecorded).IsValid);

        foreach (var environment in new[] { DeploymentEnvironmentId.DevEnv, DeploymentEnvironmentId.TestEnv, DeploymentEnvironmentId.ProdEnv })
        {
            var readiness = ReleaseSecurityPlanValidation.AssessReadiness(unrecorded, environment);

            Assert.False(readiness.IsReady, $"{environment} must not be satisfied by an unrecorded mechanism.");
            Assert.True(readiness.RefusedBecause(ReleasePlanRefusalReason.NoProtectionMechanismRecorded));
            Assert.True(readiness.RefusedBecause(ReleasePlanRefusalReason.RotationConfirmationNotRecorded));
        }
    }

    /// <summary>No plan at all is the same refusal, and it is not an exception — absence is a refusal.</summary>
    [Fact]
    public void NoPlanAtAll_IsNotReady_AndIsReportedRatherThanThrown()
    {
        foreach (var environment in new[] { DeploymentEnvironmentId.DevEnv, DeploymentEnvironmentId.TestEnv, DeploymentEnvironmentId.ProdEnv })
        {
            var readiness = ReleaseSecurityPlanValidation.AssessReadiness(null, environment);

            Assert.False(readiness.IsReady);
            Assert.True(readiness.RefusedBecause(ReleasePlanRefusalReason.NoProtectionMechanismRecorded));
            Assert.True(readiness.RefusedBecause(ReleasePlanRefusalReason.RotationConfirmationNotRecorded));
        }
    }

    // =====================================================================================================
    // TASK 3 — each rule, with the negative control that makes it a rule rather than a sentence
    // =====================================================================================================

    [Fact]
    public void C1_ConfirmedRotation_RequiresADecisionReference()
    {
        var refused = ReleaseSecurityPlanValidation.Validate(W9State() with { CredentialRotationDecisionReference = null });

        Assert.False(refused.IsValid);
        Assert.True(refused.RefusedBecause(ReleasePlanRefusalReason.RotationConfirmationNotRecorded));

        // Positive twin: the same record with the decision restored is valid, so the refusal is attributable
        // to the missing decision and not to something else in the builder.
        Assert.True(ReleaseSecurityPlanValidation.Validate(W9State()).IsValid);
    }

    [Theory]
    [InlineData(DeploymentAuthorityRole.Builder)]
    [InlineData(DeploymentAuthorityRole.DeliveryTeam)]
    [InlineData(DeploymentAuthorityRole.OnCall)]
    public void C1_ConfirmedRotation_RequiresOwnerProvenance(DeploymentAuthorityRole role)
    {
        var refused = ReleaseSecurityPlanValidation.Validate(W9State() with { HumanDecisionAuthority = role });

        Assert.False(refused.IsValid);

        // The rotation refusal and the protection refusal are separate reasons; both are expected here
        // because one authority member backs both decisions, and a reader must be able to see which failed.
        Assert.True(refused.RefusedBecause(ReleasePlanRefusalReason.RotationConfirmationNotOwnerAuthorized));
        Assert.True(refused.RefusedBecause(ReleasePlanRefusalReason.ProtectionMechanismNotOwnerAuthorized));
    }

    [Fact]
    public void C1_ARotationDecision_AgainstAnUnconfirmedState_IsRefused()
    {
        var refused = ReleaseSecurityPlanValidation.Validate(
            W9State() with { CredentialRotationStatus = CredentialRotationStatus.Required });

        Assert.False(refused.IsValid);
        Assert.True(refused.RefusedBecause(ReleasePlanRefusalReason.RotationDecisionReferenceOnUnconfirmedState));
    }

    [Fact]
    public void C2_CompensatingControl_RequiresApproval()
    {
        var refused = ReleaseSecurityPlanValidation.Validate(W9State() with { CompensatingControlApproved = false });

        Assert.False(refused.IsValid);
        Assert.True(refused.RefusedBecause(ReleasePlanRefusalReason.CompensatingControlNotApproved));
    }

    /// <summary>
    /// An approved control that was never exercised is a plan, not a control — the distinction that made
    /// W9.4's C-2 finding a finding rather than a formality.
    /// </summary>
    [Fact]
    public void C2_CompensatingControl_RequiresVerification()
    {
        var refused = ReleaseSecurityPlanValidation.Validate(W9State() with { CompensatingControlVerified = false });

        Assert.False(refused.IsValid);
        Assert.True(refused.RefusedBecause(ReleasePlanRefusalReason.CompensatingControlNotVerified));
    }

    [Fact]
    public void C2_CompensatingControl_RequiresAnExplicitScope()
    {
        var refused = ReleaseSecurityPlanValidation.Validate(
            W9State() with { AllowedEnvironmentScope = ReleaseReferenceProtectionScope.None });

        Assert.False(refused.IsValid);
        Assert.True(refused.RefusedBecause(ReleasePlanRefusalReason.CompensatingControlScopeMissing));
    }

    /// <summary>
    /// <b>The DEV/TEST control cannot be widened into PROD authority.</b> The scope enum has no PROD member
    /// today; this asserts that the record is refused rather than narrowed if one is ever added, because
    /// silently narrowing is how a record comes to say one thing while the behaviour does another.
    /// </summary>
    [Theory]
    [InlineData(ReleaseReferenceProtectionScope.AllRatifiedEnvironments)]
    public void C2_ACompensatingControlScopedBeyondDevTest_IsRefusedNotNarrowed(ReleaseReferenceProtectionScope scope)
    {
        var refused = ReleaseSecurityPlanValidation.Validate(W9State() with { AllowedEnvironmentScope = scope });

        Assert.False(refused.IsValid);
        Assert.True(refused.RefusedBecause(ReleasePlanRefusalReason.CompensatingControlScopeExceedsDevTest));
    }

    [Fact]
    public void C2_AnOwnerlessProtectionMechanism_IsRefused()
    {
        foreach (var plan in new[]
                 {
                     W9State() with { HumanDecisionReference = null },
                     W9State() with { HumanDecisionAuthority = DeploymentAuthorityRole.DeliveryTeam }
                 })
        {
            var refused = ReleaseSecurityPlanValidation.Validate(plan);

            Assert.False(refused.IsValid);
            Assert.True(refused.RefusedBecause(ReleasePlanRefusalReason.ProtectionMechanismNotOwnerAuthorized));
        }
    }

    [Fact]
    public void ServerSideProtection_MayOnlyBeRecordedWhenItWasActuallyVerified()
    {
        var claimed = W9State() with
        {
            ReleaseReferenceProtectionMode = ReleaseReferenceProtectionMode.ServerSideProtected,
            CompensatingControlApproved = false,
            CompensatingControlVerified = false,
            AllowedEnvironmentScope = ReleaseReferenceProtectionScope.None,
            ReleaseRefServerSideProtectionVerified = false
        };

        var refused = ReleaseSecurityPlanValidation.Validate(claimed);

        Assert.False(refused.IsValid);
        Assert.True(refused.RefusedBecause(ReleasePlanRefusalReason.ServerSideProtectionNotVerified));

        // Positive twin: the same shape with a genuine observation is well-formed. The rule is about the
        // observation, not about forbidding the mechanism.
        var observed = claimed with { ReleaseRefServerSideProtectionVerified = true };

        Assert.True(ReleaseSecurityPlanValidation.Validate(observed).IsValid);
    }

    [Fact]
    public void ServerSideProtection_CannotBeScopedToDevTest()
    {
        var refused = ReleaseSecurityPlanValidation.Validate(W9State() with
        {
            ReleaseReferenceProtectionMode = ReleaseReferenceProtectionMode.ServerSideProtected,
            CompensatingControlApproved = false,
            CompensatingControlVerified = false,
            ReleaseRefServerSideProtectionVerified = true,
            AllowedEnvironmentScope = ReleaseReferenceProtectionScope.DevTest
        });

        Assert.False(refused.IsValid);
        Assert.True(refused.RefusedBecause(ReleasePlanRefusalReason.ServerSideProtectionCannotBeScopedToDevTest));
    }

    [Fact]
    public void C2_ServerSideProtection_WithCompensatingControlFlags_IsRefused()
    {
        var refused = ReleaseSecurityPlanValidation.Validate(W9State() with
        {
            ReleaseReferenceProtectionMode = ReleaseReferenceProtectionMode.ServerSideProtected,
            ReleaseRefServerSideProtectionVerified = true,
            AllowedEnvironmentScope = ReleaseReferenceProtectionScope.None
        });

        Assert.False(refused.IsValid);
        Assert.True(refused.RefusedBecause(ReleasePlanRefusalReason.CompensatingControlClaimsServerSideProtection));
    }

    [Fact]
    public void AScopeWithNoMechanism_IsRefused()
    {
        var refused = ReleaseSecurityPlanValidation.Validate(W9State() with
        {
            ReleaseReferenceProtectionMode = ReleaseReferenceProtectionMode.None,
            CompensatingControlApproved = false,
            CompensatingControlVerified = false
        });

        Assert.False(refused.IsValid);
        Assert.True(refused.RefusedBecause(ReleasePlanRefusalReason.ScopeRecordedWithoutAMechanism));
    }

    // =====================================================================================================
    // Provenance and shape
    // =====================================================================================================

    [Theory]
    [InlineData("updatedBy")]
    [InlineData("reason")]
    [InlineData("updatedAt")]
    [InlineData("version")]
    [InlineData("releaseId")]
    [InlineData("schema")]
    public void AnIncoherentRecord_IsRefused(string which)
    {
        var plan = which switch
        {
            "updatedBy" => W9State() with { UpdatedBy = string.Empty },
            "reason" => W9State() with { Reason = "  " },
            "updatedAt" => W9State() with { UpdatedAt = null },
            "version" => W9State() with { Version = 0 },
            "releaseId" => W9State() with { ReleaseId = null },
            "schema" => W9State() with { SchemaVersion = ReleaseSecurityPlan.MaxSchemaVersion + 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(which))
        };

        Assert.False(ReleaseSecurityPlanValidation.Validate(plan).IsValid);
    }

    /// <summary>
    /// The canonical projection is the comparison basis for every stored hash, so a member that does not
    /// participate in it is a member whose change is undetectable.
    /// </summary>
    [Fact]
    public void TheCanonicalProjection_ChangesWhenAnyRecordedFactChanges()
    {
        var baseline = W9State().ComputePlanDigest();

        var variations = new (string Name, ReleaseSecurityPlan Plan)[]
        {
            ("rotation status", W9State() with { CredentialRotationStatus = CredentialRotationStatus.Required, CredentialRotationDecisionReference = null }),
            ("rotation decision", W9State() with { CredentialRotationDecisionReference = RotationDecision + "-2" }),
            ("mechanism", W9State() with { ReleaseReferenceProtectionMode = ReleaseReferenceProtectionMode.None, CompensatingControlApproved = false, CompensatingControlVerified = false, AllowedEnvironmentScope = ReleaseReferenceProtectionScope.None }),
            ("approved", W9State() with { CompensatingControlApproved = false }),
            ("verified", W9State() with { CompensatingControlVerified = false }),
            ("scope", W9State() with { AllowedEnvironmentScope = ReleaseReferenceProtectionScope.None }),
            ("human decision", W9State() with { HumanDecisionReference = CompensatingDecision + "-2" }),
            ("authority", W9State() with { HumanDecisionAuthority = DeploymentAuthorityRole.DeliveryTeam }),
            ("timestamp", W9State() with { UpdatedAt = DateTimeOffset.UnixEpoch }),
            ("actor", W9State() with { UpdatedBy = "someone-else" }),
            ("reason", W9State() with { Reason = "a different reason" }),
            ("version", W9State() with { Version = 2 })
        };

        foreach (var (name, plan) in variations)
        {
            Assert.NotEqual(baseline, plan.ComputePlanDigest());
        }

        // Restating the same record produces the same digest. A projection that drifted between two reads of
        // identical state could not be used as a comparison basis at all.
        Assert.Equal(baseline, W9State().ComputePlanDigest());
    }
}
