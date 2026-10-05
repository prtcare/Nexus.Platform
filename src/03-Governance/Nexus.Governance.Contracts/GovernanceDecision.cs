namespace Nexus.Governance.Contracts;

/// <summary>
/// The deterministic outcome of one evaluation.
///
/// <para><see cref="Reasons"/> is the whole point of returning a record rather than a bare verdict:
/// a refusal a caller cannot act on is indistinguishable from a broken caller. Each reason names
/// the input it came from, and the reasons are ordered by the fixed rule order, so the FIRST reason
/// is the deciding one.</para>
///
/// <para><b>No field carries an AI opinion.</b> A verdict is produced by rules over the request;
/// nothing here can be overwritten by a model, a confidence score, or a review result. The
/// <see cref="EvaluatorVersion"/> records WHICH deterministic rule set produced the verdict, so a
/// stored decision stays interpretable after the rules change.</para>
/// </summary>
public sealed record GovernanceDecision
{
    public GovernanceDecision(
        GovernanceVerdict verdict,
        IReadOnlyList<string> reasons,
        string evaluatorVersion)
    {
        Verdict = verdict;
        Reasons = reasons ?? throw new ArgumentNullException(nameof(reasons));
        EvaluatorVersion = evaluatorVersion
            ?? throw new ArgumentNullException(nameof(evaluatorVersion));
    }

    public GovernanceVerdict Verdict { get; }

    /// <summary>Deciding reason first, then the remaining findings in rule order.</summary>
    public IReadOnlyList<string> Reasons { get; }

    /// <summary>Identifies the deterministic rule set that produced this verdict.</summary>
    public string EvaluatorVersion { get; }

    public bool IsAllow => Verdict == GovernanceVerdict.Allow;

    public bool IsBlock => Verdict == GovernanceVerdict.Block;

    public bool IsHumanDecisionRequired => Verdict == GovernanceVerdict.HumanDecisionRequired;

    /// <summary>The deciding reason, or an empty string when there is none.</summary>
    public string DecidingReason => Reasons.Count == 0 ? "" : Reasons[0];

    public override string ToString() =>
        Verdict + ": " + DecidingReason;
}

/// <summary>
/// L03 GOVERNANCE deterministic evaluation port — the Platform Contract Plane surface a consuming
/// host programs against.
///
/// <para><b>Provider-neutral by construction.</b> The interface names no model, provider, gateway
/// or AI concept, and takes no AI input. An implementation is a pure function of
/// <see cref="GovernanceRequest"/>: no clock, no I/O, no randomness, no ambient state. A host that
/// wants AI evidence must attach it to its own records; it cannot route it through this port, which
/// is what makes "AI may recommend, never decide" a property of the shape rather than a promise
/// about behaviour.</para>
///
/// <para>Implementations must not block, and must not throw for any well-formed request. A request
/// the rules cannot decide returns <see cref="GovernanceVerdict.HumanDecisionRequired"/>.</para>
/// </summary>
public interface IGovernanceEvaluator
{
    /// <summary>The deterministic rule set this instance implements.</summary>
    string EvaluatorVersion { get; }

    /// <summary>Evaluates a protected operation. Pure; the same request always yields the same decision.</summary>
    GovernanceDecision Evaluate(GovernanceRequest request);
}
