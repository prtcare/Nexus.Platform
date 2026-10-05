using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>The outcome of release certification.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseCertificationVerdict
{
    /// <summary>Every required condition is met and no security action is outstanding.</summary>
    Certified,

    /// <summary>
    /// Every required <i>technical</i> condition is met, and one or more security actions are outstanding.
    ///
    /// <para>
    /// This is a distinct verdict rather than a refusal, because the two demand different responses.
    /// A refusal means the release must change; this means the release is complete and a human must act.
    /// Collapsing it into <see cref="Refused"/> would send someone to look for a defect that is not there,
    /// and collapsing it into <see cref="Certified"/> would produce a release that looks deployable and
    /// is not.
    /// </para>
    /// </summary>
    CertifiedPendingSecurityAction,

    /// <summary>A required condition failed. The reasons name which.</summary>
    Refused
}

/// <summary>
/// The result of certifying a release, with the typed reasons for anything that did not pass.
///
/// <para>
/// Like <see cref="CertificationDecision"/>, this collects <i>every</i> failing condition rather than
/// returning at the first: a release that fails four checks should say so once, not four times over four
/// attempts.
/// </para>
/// </summary>
public sealed record ReleaseCertificationDecision
{
    private ReleaseCertificationDecision(
        ReleaseCertificationVerdict verdict,
        ReleaseId? releaseId,
        IReadOnlyList<ReleaseRefusalReason> refusalReasons,
        IReadOnlyList<string> outstandingSecurityActions,
        IReadOnlyList<string> detail)
    {
        Verdict = verdict;
        ReleaseId = releaseId;
        RefusalReasons = refusalReasons;
        OutstandingSecurityActions = outstandingSecurityActions;
        Detail = detail;
    }

    public ReleaseCertificationVerdict Verdict { get; }

    public ReleaseId? ReleaseId { get; }

    /// <summary>Empty unless the verdict is <see cref="ReleaseCertificationVerdict.Refused"/>.</summary>
    public IReadOnlyList<ReleaseRefusalReason> RefusalReasons { get; }

    /// <summary>
    /// Named, human-readable security actions that must complete before the release may be published or
    /// deployed. Empty unless the verdict is <see cref="ReleaseCertificationVerdict.CertifiedPendingSecurityAction"/>.
    /// <b>Never a credential value</b> — each entry names the action, not the material.
    /// </summary>
    public IReadOnlyList<string> OutstandingSecurityActions { get; }

    public IReadOnlyList<string> Detail { get; }

    public bool IsCertified => Verdict != ReleaseCertificationVerdict.Refused;

    /// <summary>True only when the release may be deployed. A pending security action is not deployable.</summary>
    public bool IsDeployable => Verdict == ReleaseCertificationVerdict.Certified;

    public bool RefusedBecause(ReleaseRefusalReason reason) => RefusalReasons.Contains(reason);

    /// <summary>
    /// The passing verdict.
    /// </summary>
    /// <param name="detail">
    /// Optional, and supplied by the caller so a certification records <i>how</i> its conditions were met —
    /// in particular which mechanism protects the release reference, since a release certified against a
    /// server-side ruleset and one certified against the DEV/TEST compensating control are not the same
    /// release. Omitted, the verdict detail is the bare <c>RELEASE_CERTIFIED</c> marker.
    /// </param>
    public static ReleaseCertificationDecision Certified(ReleaseId releaseId, IReadOnlyList<string>? detail = null)
        => new(
            ReleaseCertificationVerdict.Certified,
            releaseId,
            [],
            [],
            detail is { Count: > 0 } ? [.. detail] : ["RELEASE_CERTIFIED"]);

    public static ReleaseCertificationDecision CertifiedPendingSecurityAction(
        ReleaseId releaseId,
        IReadOnlyList<string> outstandingSecurityActions,
        IReadOnlyList<string> detail)
    {
        if (outstandingSecurityActions is null || outstandingSecurityActions.Count == 0)
        {
            throw new ArgumentException(
                "The pending-security-action verdict must name at least one outstanding action. Without it the state is indistinguishable from a plain certification.",
                nameof(outstandingSecurityActions));
        }

        return new ReleaseCertificationDecision(
            ReleaseCertificationVerdict.CertifiedPendingSecurityAction,
            releaseId,
            [],
            [.. outstandingSecurityActions],
            detail ?? []);
    }

    public static ReleaseCertificationDecision Refuse(
        ReleaseId? releaseId,
        IReadOnlyList<ReleaseRefusalReason> reasons,
        IReadOnlyList<string> detail)
    {
        if (reasons is null || reasons.Count == 0)
        {
            throw new ArgumentException(
                "A refusal must name at least one typed reason. An unnamed refusal is indistinguishable from a bug.",
                nameof(reasons));
        }

        return new ReleaseCertificationDecision(
            ReleaseCertificationVerdict.Refused,
            releaseId,
            [.. reasons.Distinct().OrderBy(r => r)],
            [],
            detail ?? []);
    }

    public override string ToString() => Verdict switch
    {
        ReleaseCertificationVerdict.Certified => $"RELEASE_CERTIFIED {ReleaseId}",
        ReleaseCertificationVerdict.CertifiedPendingSecurityAction =>
            $"READY_FOR_DEV_PENDING_SECURITY_ACTION {ReleaseId} [{string.Join(", ", OutstandingSecurityActions)}]",
        _ => $"REFUSED [{string.Join(", ", RefusalReasons)}]"
    };
}
