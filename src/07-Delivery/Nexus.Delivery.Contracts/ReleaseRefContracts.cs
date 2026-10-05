using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// How a release ref is protected, and whether that protection is verifiable from where the assessment ran.
///
/// <para>
/// <b>The distinction between "protected" and "protection verified" is the substance of this type.</b> The
/// Owner's D2 ruling requires three things: deterministic local/pipeline refusal of unauthorized writes;
/// remote repository protection as the authoritative server-side control; and no assumption that process
/// discipline alone is sufficient. A local process can observe the first. It <i>cannot</i> observe the
/// second — a ruleset lives in the remote's settings, and a repository that has one and a repository that
/// has not installed one look identical from a checkout. So the assessment reports which of those it
/// established, and refuses when the server-side half is not verified, rather than inferring protection
/// from anything local.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseRefProtectionStatus
{
    /// <summary>No release ref exists at all. The state of this estate today.</summary>
    Absent,

    /// <summary>The ref exists and nothing protects it. A releasable state built from it would be build-once in name only.</summary>
    Unprotected,

    /// <summary>The ref exists and local/pipeline refusal is in place, but the server-side ruleset could not be observed from here.</summary>
    RequiresServerSideVerification,

    /// <summary>Both halves established: local refusal present, and the server-side protection confirmed by an authorized check.</summary>
    Protected
}

/// <summary>Why a candidate source could not be treated as governed release input.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseRefRefusalReason
{
    None = 0,

    /// <summary>No release ref exists, so there is nothing to certify against.</summary>
    NoReleaseRefExists,

    /// <summary>The ref exists but nothing refuses an unauthorized write to it.</summary>
    RefNotProtected,

    /// <summary>Server-side protection could not be verified from this stage, and is not assumed.</summary>
    ServerSideProtectionUnverified,

    /// <summary>
    /// A ref was manufactured for the purpose of passing this check.
    ///
    /// <para>
    /// This reason exists because the tempting shortcut is obvious: create a branch called
    /// <c>release</c>, point it at the commit, and declare the prerequisite met. That would turn a
    /// governance requirement into a naming convention and would be worse than reporting the gap, because
    /// the gap is at least visible. A ref that did not exist before the certification ran, and was created
    /// by it, is refused.
    /// </para>
    /// </summary>
    FabricatedRefNotPermitted,

    /// <summary>The ref tip does not contain the commit the build recorded.</summary>
    RefDoesNotContainBuildCommit
}

/// <summary>
/// A candidate release ref, as observed.
///
/// <para>
/// <see cref="ObservedBeforeCertification"/> is what makes <see cref="ReleaseRefRefusalReason.FabricatedRefNotPermitted"/>
/// enforceable: the assessment is told whether the ref already existed when the stage began, so a ref
/// created in order to satisfy the check is identifiable rather than indistinguishable.
/// </para>
/// </summary>
public sealed record ReleaseRefDescriptor(
    string RepositoryLabel,
    string RefName,
    ReleaseRefProtectionStatus ProtectionStatus,
    bool ObservedBeforeCertification,
    string? TipCommitSha = null,
    string? ProtectionMechanism = null);

/// <summary>The outcome of assessing whether a certified build may become Release Bundle input.</summary>
public sealed record ReleaseRefAssessment
{
    private ReleaseRefAssessment(
        bool isEligible,
        ReleaseRefDescriptor? descriptor,
        IReadOnlyList<ReleaseRefRefusalReason> refusalReasons,
        IReadOnlyList<string> detail)
    {
        IsEligible = isEligible;
        Descriptor = descriptor;
        RefusalReasons = refusalReasons;
        Detail = detail;
    }

    public bool IsEligible { get; }

    public ReleaseRefDescriptor? Descriptor { get; }

    public IReadOnlyList<ReleaseRefRefusalReason> RefusalReasons { get; }

    public IReadOnlyList<string> Detail { get; }

    public bool RefusedBecause(ReleaseRefRefusalReason reason) => RefusalReasons.Contains(reason);

    public static ReleaseRefAssessment Eligible(ReleaseRefDescriptor descriptor)
        => new(true, descriptor, [], [$"Governed release input: {descriptor.RepositoryLabel}:{descriptor.RefName}"]);

    public static ReleaseRefAssessment Ineligible(
        ReleaseRefDescriptor? descriptor,
        IReadOnlyList<ReleaseRefRefusalReason> reasons,
        IReadOnlyList<string> detail)
    {
        if (reasons is null || reasons.Count == 0)
        {
            throw new ArgumentException("An ineligible assessment must name at least one typed reason.", nameof(reasons));
        }

        return new ReleaseRefAssessment(false, descriptor, [.. reasons.Distinct().OrderBy(r => r)], detail ?? []);
    }
}

/// <summary>
/// Decides whether a certified build has a governed source it can be released from.
///
/// <para>
/// <b>This exists so that the W9.2 exit is not a fabricated release.</b> The stage's contract is to end
/// with <c>CERTIFIED_ARTIFACT_READY_FOR_RELEASE_BUNDLE</c> — an artifact that is certified and has a
/// defined path to becoming release input — and not with a Release Bundle. So the mechanism is
/// implemented, it is pointed at the real state of this estate, and it is expected to refuse: W9.1
/// measured that no repository has a release ref. An implementation that returned eligible today would be
/// reporting a fact that does not exist.
/// </para>
/// </summary>
public interface IReleaseRefPolicy
{
    Task<ReleaseRefAssessment> AssessAsync(
        string repositoryLabel,
        string repositoryRoot,
        string buildCommitSha,
        CancellationToken cancellationToken = default);
}
