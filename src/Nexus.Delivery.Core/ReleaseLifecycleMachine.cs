using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The release lifecycle machine: how a release moves from a certified build to DEV readiness.
/// <b>Pure, total and deterministic.</b>
///
/// <para>
/// Every member of <see cref="ReleaseTransition"/> has a gate, and the gates are total over the enum: a new
/// transition without one is a compile-visible hole rather than a silent default that quietly allows it.
/// There is no I/O, no clock and no ambient state — the machine decides, the caller observes and records.
/// Same construction as <see cref="DeploymentStateMachine"/>, for the same reason: a decision that can be
/// re-derived from a record is a decision someone else can check.
/// </para>
///
/// <para>
/// <b>The certification judgement is not reimplemented here.</b> <see cref="ReleaseTransition.CertifyRelease"/>
/// and both readiness transitions delegate to <see cref="ReleaseCertificationGate"/>. A second copy of "is
/// this release certifiable" would drift from the first, and the estate has already paid for that defect
/// once: the W9.2 secret scanner judged "is this a value?" in two places, the two disagreed, and a refusal
/// came out of the pair that neither would have produced alone.
/// </para>
///
/// <para>
/// <b>Absence of evidence is a refusal.</b> The gates read nullable observations, so "not observed" cannot
/// be read as "healthy" — following the estate's rule that a thing which could not be checked is reported
/// as <c>NOT_RUN</c> and never as a pass.
/// </para>
/// </summary>
public static class ReleaseLifecycleMachine
{
    /// <summary>
    /// Validates the certified build the release is built on. Anchored at
    /// <see cref="ReleaseLifecycleState.BuildCertified"/>: a certified build has no prior release state, so
    /// it is a factory rather than a transition.
    /// </summary>
    public static ReleaseDecision CertifyBuild(ReleaseGateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var reasons = new List<ReleaseRefusalReason>();

        if (evidence.BuildIsCertified is null)
        {
            reasons.Add(ReleaseRefusalReason.EvidenceIncomplete);
        }
        else if (evidence.BuildIsCertified == false)
        {
            reasons.Add(ReleaseRefusalReason.BuildNotCertified);
        }

        if (evidence.SourceLineageComplete is null)
        {
            reasons.Add(ReleaseRefusalReason.EvidenceIncomplete);
        }
        else if (evidence.SourceLineageComplete == false)
        {
            reasons.Add(ReleaseRefusalReason.ReleaseBundleIncomplete);
        }

        return reasons.Count > 0
            ? ReleaseDecision.Refuse(ReleaseTransition.CertifyBuild, ReleaseLifecycleState.Refused, [.. reasons])
            : ReleaseDecision.Allow(
                ReleaseTransition.CertifyBuild,
                ReleaseLifecycleState.BuildCertified,
                ReleaseLifecycleState.BuildCertified);
    }

    /// <summary>Evaluates one transition. The only entry point for transitions; all gating lives here.</summary>
    /// <param name="current">The state the release occupies.</param>
    /// <param name="transition">The transition requested.</param>
    /// <param name="release">
    /// The release record, required by the transitions that judge the release itself. Null is refused with
    /// <see cref="ReleaseRefusalReason.EvidenceIncomplete"/> rather than treated as an absent constraint.
    /// </param>
    /// <param name="evidence">What the caller observed.</param>
    public static ReleaseDecision Decide(
        ReleaseLifecycleState current,
        ReleaseTransition transition,
        ReleaseRecord? release,
        ReleaseGateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        // A withdrawal is Owner-reserved and terminal. Checked before everything else so the reason a
        // reader sees is the true one, rather than a downstream gate's complaint.
        if (current == ReleaseLifecycleState.Withdrawn && transition != ReleaseTransition.Withdraw)
        {
            return ReleaseDecision.Refuse(transition, current, ReleaseRefusalReason.ReleaseIsWithdrawn);
        }

        if (transition == ReleaseTransition.CertifyBuild)
        {
            return CertifyBuild(evidence);
        }

        var expectedNext = NextStateFor(transition);

        if (expectedNext is null)
        {
            return ReleaseDecision.Refuse(transition, current, ReleaseRefusalReason.IllegalTransition);
        }

        var validFrom = PredecessorStatesFor(transition) ?? [];

        if (validFrom.Count > 0 && !validFrom.Contains(current))
        {
            return ReleaseDecision.Refuse(transition, current, ReleaseRefusalReason.IllegalTransition);
        }

        var reasons = EvaluateGates(transition, release, evidence);

        return reasons.Count > 0
            ? ReleaseDecision.Refuse(transition, current, [.. reasons])
            : ReleaseDecision.Allow(transition, current, expectedNext.Value);
    }

    /// <summary>
    /// The state a transition lands in. <see langword="null"/> for an unknown member, which is how a future
    /// enum addition fails closed.
    ///
    /// <para>
    /// Both readiness transitions are separate members landing in separate states, rather than one member
    /// landing in one of two. That keeps this function single-valued and total, and it means the choice
    /// between "ready" and "ready pending a security action" is made by a gate that can refuse — not by a
    /// branch that always produces something.
    /// </para>
    /// </summary>
    public static ReleaseLifecycleState? NextStateFor(ReleaseTransition transition) => transition switch
    {
        ReleaseTransition.CertifyBuild => ReleaseLifecycleState.BuildCertified,
        ReleaseTransition.RegisterArtifact => ReleaseLifecycleState.ArtifactRegistered,
        ReleaseTransition.DraftRelease => ReleaseLifecycleState.ReleaseDraft,
        ReleaseTransition.CertifyRelease => ReleaseLifecycleState.ReleaseCertified,
        ReleaseTransition.DeclareReadyForDev => ReleaseLifecycleState.ReadyForDev,
        ReleaseTransition.DeclareReadyForDevPendingSecurityAction => ReleaseLifecycleState.ReadyForDevPendingSecurityAction,
        ReleaseTransition.Refuse => ReleaseLifecycleState.Refused,
        ReleaseTransition.Withdraw => ReleaseLifecycleState.Withdrawn,
        _ => null
    };

    /// <summary>
    /// The states a transition may originate from. Empty means "from anywhere"; <see langword="null"/> means
    /// the transition is unknown (the <c>CertifyBuild</c> factory), and callers treat that as no constraint
    /// because it is evaluated by <see cref="CertifyBuild"/> instead.
    /// </summary>
    public static IReadOnlyList<ReleaseLifecycleState>? PredecessorStatesFor(ReleaseTransition transition) => transition switch
    {
        ReleaseTransition.CertifyBuild => null,

        ReleaseTransition.RegisterArtifact => [ReleaseLifecycleState.BuildCertified],
        ReleaseTransition.DraftRelease => [ReleaseLifecycleState.ArtifactRegistered],
        ReleaseTransition.CertifyRelease => [ReleaseLifecycleState.ReleaseDraft],
        ReleaseTransition.DeclareReadyForDev => [ReleaseLifecycleState.ReleaseCertified],
        ReleaseTransition.DeclareReadyForDevPendingSecurityAction => [ReleaseLifecycleState.ReleaseCertified],

        // A refusal is an observation, not a decision, so it may fire from any state that has not already
        // reached a terminal one.
        ReleaseTransition.Refuse =>
        [
            ReleaseLifecycleState.BuildCertified,
            ReleaseLifecycleState.ArtifactRegistered,
            ReleaseLifecycleState.ReleaseDraft,
            ReleaseLifecycleState.ReleaseCertified
        ],

        ReleaseTransition.Withdraw => [], // from anywhere, Owner-reserved

        _ => null
    };

    private static List<ReleaseRefusalReason> EvaluateGates(
        ReleaseTransition transition,
        ReleaseRecord? release,
        ReleaseGateEvidence evidence)
    {
        var reasons = new List<ReleaseRefusalReason>();

        switch (transition)
        {
            case ReleaseTransition.RegisterArtifact:
                // The bytes must hash to the digest the build recorded. Null is not observed; false is a
                // corruption finding, and the two are reported differently for that reason.
                if (evidence.ArtifactHashMatchesManifest is null)
                {
                    reasons.Add(ReleaseRefusalReason.EvidenceIncomplete);
                }
                else if (evidence.ArtifactHashMatchesManifest == false)
                {
                    reasons.Add(ReleaseRefusalReason.ArtifactHashMismatch);
                }

                if (evidence.BuildIsCertified is not true)
                {
                    reasons.Add(evidence.BuildIsCertified is null
                        ? ReleaseRefusalReason.EvidenceIncomplete
                        : ReleaseRefusalReason.BuildNotCertified);
                }

                break;

            case ReleaseTransition.DraftRelease:
                if (release is null)
                {
                    reasons.Add(ReleaseRefusalReason.EvidenceIncomplete);
                    break;
                }

                // Judged from the record, not asserted by the caller.
                foreach (var _ in ReleaseCertificationGate.MissingBundleElements(release))
                {
                    reasons.Add(ReleaseRefusalReason.ReleaseBundleIncomplete);
                    break;
                }

                break;

            case ReleaseTransition.CertifyRelease:
                reasons.AddRange(CertificationReasons(release, evidence));
                break;

            case ReleaseTransition.DeclareReadyForDev:
                reasons.AddRange(CertificationReasons(release, evidence));

                if (reasons.Count == 0
                    && release is not null
                    && ReleaseCertificationGate.Evaluate(release, evidence).Verdict
                        == ReleaseCertificationVerdict.CertifiedPendingSecurityAction)
                {
                    reasons.Add(ReleaseRefusalReason.SecurityActionOutstanding);
                }

                break;

            case ReleaseTransition.DeclareReadyForDevPendingSecurityAction:
                reasons.AddRange(CertificationReasons(release, evidence));

                if (reasons.Count == 0 && release is not null)
                {
                    var verdict = ReleaseCertificationGate.Evaluate(release, evidence).Verdict;

                    // Declaring the pending state with nothing outstanding would make it a way around the
                    // unqualified readiness gate, which is the one thing it must not be.
                    if (verdict != ReleaseCertificationVerdict.CertifiedPendingSecurityAction)
                    {
                        reasons.Add(ReleaseRefusalReason.NoOutstandingSecurityAction);
                    }
                }

                break;

            case ReleaseTransition.Refuse:
            case ReleaseTransition.Withdraw:
                if (string.IsNullOrWhiteSpace(evidence.Reason))
                {
                    // A refusal or a withdrawal is a decision, and a decision must say why it was made.
                    reasons.Add(ReleaseRefusalReason.EvidenceIncomplete);
                }

                if (transition == ReleaseTransition.Withdraw && evidence.Authorization?.IsOwnerAuthorized != true)
                {
                    reasons.Add(ReleaseRefusalReason.OwnerAuthorizationRequired);
                }

                break;

            default:
                break;
        }

        return reasons;
    }

    /// <summary>
    /// The certification judgement, taken from <see cref="ReleaseCertificationGate"/> and nowhere else.
    /// A refusal's typed reasons are carried through unchanged so the machine and the gate never disagree
    /// about <i>why</i>, only about what may follow.
    /// </summary>
    private static IEnumerable<ReleaseRefusalReason> CertificationReasons(ReleaseRecord? release, ReleaseGateEvidence evidence)
    {
        if (release is null)
        {
            yield return ReleaseRefusalReason.EvidenceIncomplete;
            yield break;
        }

        var decision = ReleaseCertificationGate.Evaluate(release, evidence);

        if (decision.Verdict == ReleaseCertificationVerdict.Refused)
        {
            foreach (var reason in decision.RefusalReasons)
            {
                yield return reason;
            }
        }
    }
}
