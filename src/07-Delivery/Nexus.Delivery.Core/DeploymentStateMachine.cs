using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The deployment/promotion state machine. <b>Pure, total and deterministic.</b>
///
/// <para>
/// Every member of <see cref="DeploymentTransition"/> has a gate here, and the gates are total over
/// the enum: a new transition without a gate is a compile-visible hole rather than a silent default
/// that quietly allows it. There is no I/O, no clock, no ambient state — the machine decides; the
/// caller observes and records. That separation is what makes the decision reproducible from a
/// lineage record, which is invariant L-4.
/// </para>
///
/// <para>
/// <b>Absence of evidence is a refusal.</b> <see cref="DeploymentGateEvidence"/> members are nullable
/// so that "not observed" cannot be read as "healthy". At present this estate has no unit that reports
/// its own build id or digest (W9.0 F-7.1), so several gates cannot yet be satisfied from a running
/// process at all. The machine says so with a typed reason instead of proceeding, following the
/// estate's rule that a thing which could not be checked is reported as <c>NOT_RUN</c> and never as a
/// pass.
/// </para>
/// </summary>
public static class DeploymentStateMachine
{
    /// <summary>
    /// Validates the build itself. Anchored at <see cref="PromotionState.Built"/>: a build has no
    /// prior deployment state, so it is a factory rather than a transition, and modelling it as a
    /// transition would require a sentinel state that no bundle ever occupies.
    /// </summary>
    public static DeploymentDecision ValidateBuild(ReleaseBundle bundle, DeploymentGateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(evidence);

        var reasons = new List<DeploymentRefusalReason>();

        if (bundle.SchemaVersion < ReleaseBundle.MinSchemaVersion || bundle.SchemaVersion > ReleaseBundle.MaxSchemaVersion)
        {
            reasons.Add(DeploymentRefusalReason.BundleSchemaUnsupported);
        }

        // A build with no working-tree observation cannot claim a clean source. Refused, not assumed.
        if (evidence.WorkingTreeIsDirty is null)
        {
            reasons.Add(DeploymentRefusalReason.EvidenceIncomplete);
        }
        else if (evidence.WorkingTreeIsDirty == true)
        {
            reasons.Add(DeploymentRefusalReason.SourceWorkingTreeIsDirty);
        }

        if (reasons.Count > 0)
        {
            return DeploymentDecision.Refuse(DeploymentTransition.Build, PromotionState.Built, [.. reasons]);
        }

        return DeploymentDecision.Allow(DeploymentTransition.Build, PromotionState.Built, PromotionState.Built);
    }

    /// <summary>Evaluates one transition. The only entry point; all gating lives here.</summary>
    public static DeploymentDecision Decide(PromotionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A quarantined bundle is inert. Only the Owner-reserved clear may proceed, and it is checked
        // before EVERYTHING else — including the build factory route below — so that the reason a
        // reader sees is the true one. Routing Build first would let a quarantine be side-stepped by
        // asking the machine to re-validate a build, which is not a transition any caller should be
        // able to use to get past a quarantine.
        if (request.CurrentState == PromotionState.Quarantined
            && request.Transition != DeploymentTransition.ClearQuarantine)
        {
            return DeploymentDecision.Refuse(
                request.Transition,
                request.CurrentState,
                DeploymentRefusalReason.BundleQuarantined);
        }

        // A build is a factory, not a transition: it has no prior deployment state to move from.
        // Routing it here rather than through the table keeps its gate (the dirty-tree check) from
        // being skipped by a table whose "may originate anywhere" branch it would otherwise fall into.
        if (request.Transition == DeploymentTransition.Build)
        {
            return ValidateBuild(request.Bundle, request.Evidence);
        }

        var expectedNext = NextStateFor(request.Transition);

        if (expectedNext is null)
        {
            return DeploymentDecision.Refuse(
                request.Transition,
                request.CurrentState,
                DeploymentRefusalReason.IllegalTransition);
        }

        // A transition with no declared predecessor (Build) or one that may fire from anywhere
        // (Fail, Quarantine) is legal from any state; everything else must originate where the
        // table says.
        var validFrom = PredecessorStatesFor(request.Transition) ?? [];

        if (validFrom.Count > 0 && !validFrom.Contains(request.CurrentState))
        {
            return DeploymentDecision.Refuse(
                request.Transition,
                request.CurrentState,
                DeploymentRefusalReason.IllegalTransition);
        }

        var reasons = EvaluateGates(request);

        return reasons.Count > 0
            ? DeploymentDecision.Refuse(request.Transition, request.CurrentState, [.. reasons])
            : DeploymentDecision.Allow(request.Transition, request.CurrentState, expectedNext.Value);
    }

    /// <summary>
    /// The state a transition lands in. <see langword="null"/> for an unknown member, which is how a
    /// future enum addition fails closed.
    /// </summary>
    public static PromotionState? NextStateFor(DeploymentTransition transition) => transition switch
    {
        DeploymentTransition.Build => PromotionState.Built,
        DeploymentTransition.Register => PromotionState.Registered,
        DeploymentTransition.DeployToDev => PromotionState.DeployedDev,
        DeploymentTransition.VerifyInDev => PromotionState.VerifiedDev,
        DeploymentTransition.PromoteToTest => PromotionState.DeployedTest,
        DeploymentTransition.VerifyInTest => PromotionState.VerifiedTest,
        DeploymentTransition.PromoteToProd => PromotionState.DeployedProd,
        DeploymentTransition.VerifyInProd => PromotionState.Live,
        DeploymentTransition.Fail => PromotionState.Failed,
        DeploymentTransition.Rollback => PromotionState.RolledBack,
        DeploymentTransition.Quarantine => PromotionState.Quarantined,

        // A cleared bundle resumes from Registered and must re-verify in every environment it had
        // reached. Returning it to its prior state would let a quarantine be cleared and the bundle
        // immediately promoted onward without re-observation, which is the one outcome a quarantine
        // exists to prevent.
        DeploymentTransition.ClearQuarantine => PromotionState.Registered,

        _ => null
    };

    /// <summary>
    /// The states a transition may originate from. Empty means "from anywhere"; <see langword="null"/>
    /// means the transition is unknown (Build), and callers treat that as no constraint because it is
    /// evaluated by <see cref="ValidateBuild"/> instead.
    /// </summary>
    public static IReadOnlyList<PromotionState>? PredecessorStatesFor(DeploymentTransition transition) => transition switch
    {
        DeploymentTransition.Build => null,

        DeploymentTransition.Register => [PromotionState.Built],
        DeploymentTransition.DeployToDev => [PromotionState.Registered],
        DeploymentTransition.VerifyInDev => [PromotionState.DeployedDev],
        DeploymentTransition.PromoteToTest => [PromotionState.VerifiedDev],
        DeploymentTransition.VerifyInTest => [PromotionState.DeployedTest],
        DeploymentTransition.PromoteToProd => [PromotionState.VerifiedTest],
        DeploymentTransition.VerifyInProd => [PromotionState.DeployedProd],
        DeploymentTransition.Rollback => [PromotionState.Live],

        // A failure is an observation, not a decision, so it may fire from any live state.
        DeploymentTransition.Fail =>
        [
            PromotionState.Built, PromotionState.Registered,
            PromotionState.DeployedDev, PromotionState.VerifiedDev,
            PromotionState.DeployedTest, PromotionState.VerifiedTest,
            PromotionState.DeployedProd, PromotionState.Live
        ],

        DeploymentTransition.Quarantine => [], // from anywhere
        DeploymentTransition.ClearQuarantine => [PromotionState.Quarantined],

        _ => null
    };

    private static List<DeploymentRefusalReason> EvaluateGates(PromotionRequest request)
    {
        var evidence = request.Evidence;
        var reasons = new List<DeploymentRefusalReason>();

        switch (request.Transition)
        {
            case DeploymentTransition.Register:
                GateRegister(request, reasons);
                break;

            case DeploymentTransition.DeployToDev:
                GateDeployment(request, evidence, reasons);
                break;

            case DeploymentTransition.VerifyInDev:
            case DeploymentTransition.VerifyInTest:
                GateVerification(request, evidence, reasons);
                break;

            case DeploymentTransition.PromoteToTest:
                GatePromotion(request, evidence, reasons);
                break;

            case DeploymentTransition.PromoteToProd:
                if (evidence.Authorization?.IsOwnerAuthorized != true)
                {
                    reasons.Add(DeploymentRefusalReason.OwnerAuthorizationRequired);
                }

                GatePromotion(request, evidence, reasons);
                break;

            case DeploymentTransition.VerifyInProd:
                if (evidence.ReadinessObserved is null)
                {
                    reasons.Add(DeploymentRefusalReason.ReadinessNotObserved);
                }
                else if (!evidence.ReadinessObserved.Value)
                {
                    reasons.Add(DeploymentRefusalReason.ReadinessNotObserved);
                }

                // Same requirement as the DEV/TEST verification, for the same measured reason.
                if (evidence.SmokeObserved != true)
                {
                    reasons.Add(DeploymentRefusalReason.SmokeNotObserved);
                }

                break;

            case DeploymentTransition.Rollback:
                GateRollback(request, evidence, reasons);
                break;

            case DeploymentTransition.Quarantine:
                if (string.IsNullOrWhiteSpace(evidence.Reason))
                {
                    reasons.Add(DeploymentRefusalReason.EvidenceIncomplete);
                }

                // Requesting a quarantine is available to the Owner and to on-call; clearing it is not.
                if (evidence.Authorization is null)
                {
                    reasons.Add(DeploymentRefusalReason.EvidenceIncomplete);
                }

                break;

            case DeploymentTransition.ClearQuarantine:
                if (evidence.Authorization?.IsOwnerAuthorized != true)
                {
                    reasons.Add(DeploymentRefusalReason.OwnerAuthorizationRequired);
                }

                if (string.IsNullOrWhiteSpace(evidence.Reason))
                {
                    reasons.Add(DeploymentRefusalReason.EvidenceIncomplete);
                }

                break;

            case DeploymentTransition.Fail:
            case DeploymentTransition.Build:
            default:
                break;
        }

        return reasons;
    }

    private static void GateRegister(PromotionRequest request, List<DeploymentRefusalReason> reasons)
    {
        var bundle = request.Bundle;

        if (bundle.SchemaVersion < ReleaseBundle.MinSchemaVersion || bundle.SchemaVersion > ReleaseBundle.MaxSchemaVersion)
        {
            reasons.Add(DeploymentRefusalReason.BundleSchemaUnsupported);
        }

        if (request.Evidence.WorkingTreeIsDirty is null)
        {
            reasons.Add(DeploymentRefusalReason.EvidenceIncomplete);
        }
        else if (request.Evidence.WorkingTreeIsDirty == true)
        {
            reasons.Add(DeploymentRefusalReason.SourceWorkingTreeIsDirty);
        }

        if (request.BundleIdAlreadyRegistered)
        {
            reasons.Add(DeploymentRefusalReason.RegistryRefused);
        }

        if (request.ExcludedUnitsInBundle.Count > 0)
        {
            reasons.Add(DeploymentRefusalReason.UnitExcluded);
        }
    }

    private static void GateDeployment(
        PromotionRequest request,
        DeploymentGateEvidence evidence,
        List<DeploymentRefusalReason> reasons)
    {
        // There is deliberately NO ratified-environment check here. DeploymentEnvironmentId can only
        // be built by Parse or by one of the three ratified members, so an unratified environment is
        // unrepresentable and a check would be a guard that cannot fail — the exact defect class this
        // estate keeps re-finding. DeploymentRefusalReason.EnvironmentNotRatified remains in the
        // vocabulary for the boundary that does parse strings (configuration and manifests).
        if (request.ExcludedUnitsInBundle.Count > 0)
        {
            reasons.Add(DeploymentRefusalReason.UnitExcluded);
        }

        // A deployment states what it will be promoted on later; without the observation the
        // verification step that follows is unevaluable.
        if (evidence.ReadinessObserved is null)
        {
            reasons.Add(DeploymentRefusalReason.EvidenceIncomplete);
        }
    }

    private static void GateVerification(
        PromotionRequest request,
        DeploymentGateEvidence evidence,
        List<DeploymentRefusalReason> reasons)
    {
        GateDeployment(request, evidence, reasons);

        if (evidence.ReadinessObserved == false)
        {
            reasons.Add(DeploymentRefusalReason.ReadinessNotObserved);
        }

        // Owner Decision 3 (W9.4 instrument remediation). A verification requires BOTH observations.
        //
        // `!= true` rather than `== false` on purpose: null means the observation was never taken, and a
        // gate that only refused `false` would let an unobserved run through — which is precisely the
        // W9.4 false pass, where the driver's smoke verdict existed but no decision consulted it.
        if (evidence.SmokeObserved != true)
        {
            reasons.Add(DeploymentRefusalReason.SmokeNotObserved);
        }

        if (evidence.ObservedDigests is null)
        {
            reasons.Add(DeploymentRefusalReason.EvidenceIncomplete);
        }
        else
        {
            foreach (var artifact in request.Bundle.AllArtifacts)
            {
                if (!evidence.ObservedDigests.TryGetValue(artifact.UnitId, out var observed))
                {
                    reasons.Add(DeploymentRefusalReason.EvidenceIncomplete);
                    continue;
                }

                if (observed != artifact.Digest)
                {
                    reasons.Add(DeploymentRefusalReason.DigestMismatchInTarget);
                }
            }
        }

        GateMigration(evidence, reasons);
    }

    private static void GatePromotion(
        PromotionRequest request,
        DeploymentGateEvidence evidence,
        List<DeploymentRefusalReason> reasons)
    {
        GateVerification(request, evidence, reasons);

        if (evidence.ConfigurationSchemaSatisfied is null)
        {
            reasons.Add(DeploymentRefusalReason.EvidenceIncomplete);
        }
        else if (evidence.ConfigurationSchemaSatisfied == false)
        {
            reasons.Add(DeploymentRefusalReason.ConfigurationSchemaUnsatisfied);
        }

        if (evidence.ReleaseRefIsProtected is null)
        {
            reasons.Add(DeploymentRefusalReason.EvidenceIncomplete);
        }
        else if (evidence.ReleaseRefIsProtected == false)
        {
            reasons.Add(DeploymentRefusalReason.ReleaseRefNotProtected);
        }

        // The second half of the same question, asked of the same judgement the certification gate uses, once
        // per environment this promotion touches. Rule 2 above says a refusal against unauthorized writes
        // exists; this says WHICH mechanism provides it and whether that mechanism extends as far as the
        // promotion goes. The Owner-approved C-2 compensating control stops at ENV-TEST, so a production
        // promotion cannot be carried by it — which is what keeps the deviation from becoming a permanent
        // substitute for the server-side control in the one environment where it matters most.
        //
        // Both checks are retained. Neither may be removed on the grounds that the other "already covers it":
        // a ruleset is absent today on every repository here, so `ReleaseRefIsProtected == true` is a statement
        // about local and pipeline refusal, not about the remote — and the reasoning that one check makes the
        // other redundant is exactly how the stronger of two controls disappears during a refactor.
        foreach (var environment in PromotionEnvironments(request))
        {
            var protection = evidence.ReleaseReferenceProtection.Judge(environment);

            if (!protection.IsSatisfied)
            {
                reasons.Add(protection.IsUnobserved
                    ? DeploymentRefusalReason.EvidenceIncomplete
                    : DeploymentRefusalReason.ReleaseRefNotProtected);

                break;
            }
        }
    }

    /// <summary>
    /// Every environment a promotion touches: the one the caller declared, and the one the <b>transition
    /// itself</b> changes.
    ///
    /// <para>
    /// <b>Both, because either alone can be wrong.</b> The declared target is what the lineage record writes
    /// down and therefore what a reader will believe; the transition is what the step actually is. A
    /// production promotion that declared ENV-TEST would satisfy an environment check that read only the
    /// declaration — and that is not a hypothetical shape for a mistake to take, it is the shape of a mistake
    /// that would put an unreleased bundle into production under a DEV/TEST-only deviation. Consulting both is
    /// one rule rather than two: the recorded mechanism must cover every environment the promotion touches.
    /// </para>
    ///
    /// <para>
    /// A transition that changes no environment contributes nothing, which is stated as <c>null</c> rather
    /// than defaulted to a ratified environment — a default here would be an environment nobody chose.
    /// </para>
    /// </summary>
    private static IReadOnlyList<DeploymentEnvironmentId> PromotionEnvironments(PromotionRequest request)
    {
        var implied = request.Transition switch
        {
            DeploymentTransition.PromoteToTest => DeploymentEnvironmentId.TestEnv,
            DeploymentTransition.PromoteToProd => DeploymentEnvironmentId.ProdEnv,
            _ => null
        };

        return implied is null || implied == request.TargetEnvironment
            ? [request.TargetEnvironment]
            : [request.TargetEnvironment, implied];
    }

    private static void GateMigration(DeploymentGateEvidence evidence, List<DeploymentRefusalReason> reasons)
    {
        switch (evidence.MigrationCompatibility)
        {
            case null:
                reasons.Add(DeploymentRefusalReason.EvidenceIncomplete);
                break;
            case Contracts.MigrationCompatibility.Ahead:
                reasons.Add(DeploymentRefusalReason.MigrationSetAhead);
                break;
            case Contracts.MigrationCompatibility.Divergent:
                reasons.Add(DeploymentRefusalReason.MigrationSetDivergent);
                break;
        }
    }

    private static void GateRollback(PromotionRequest request, DeploymentGateEvidence evidence, List<DeploymentRefusalReason> reasons)
    {
        var plan = RollbackPlan.Plan(new RollbackRequest(
            CurrentBundleId: request.Bundle.BundleId,
            PreviousBundleId: evidence.PreviousBundle?.BundleId,
            TargetEnvironment: request.TargetEnvironment,
            PreviousBundleAvailable: evidence.PreviousBundleAvailable == true,
            CrossesMigrationBoundary: evidence.MigrationCompatibility is Contracts.MigrationCompatibility.Ahead
                or Contracts.MigrationCompatibility.Divergent,
            Rehearsed: evidence.RollbackRehearsed == true,
            Authorization: evidence.Authorization,
            Reason: evidence.Reason));

        if (!plan.IsPermitted)
        {
            reasons.AddRange(plan.RefusalReasons);
        }
    }
}
