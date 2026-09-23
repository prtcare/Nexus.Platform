using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>Runs an external process and captures its outcome. Abstracted so build logic is testable without spawning anything.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken = default);
}

/// <summary>The captured result of a process.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Assesses whether a repository has a governed release ref a certified build could be released from.
///
/// <para>
/// <b>This is implemented, and today it refuses everything — deliberately.</b> Task 11 requires the
/// mechanism to exist and forbids faking a release ref. W9.1 measured that no repository in this estate
/// has one, so the honest behaviour of a working implementation is a refusal naming exactly what is
/// missing. A policy that returned eligible today would be reporting a fact that does not exist, and the
/// temptation it avoids is specific and obvious: create a branch called <c>release</c>, point it at the
/// commit, declare the prerequisite met. That converts a governance requirement into a naming convention,
/// so a ref that did not exist when the stage began is refused as fabricated.
/// </para>
///
/// <para>
/// <b>Protection is deliberately not inferred.</b> A local process cannot see a remote ruleset — a
/// repository with server-side protection and one without look identical from a checkout. So even when a
/// release ref exists and contains the build commit, the assessment reports
/// <see cref="ReleaseRefProtectionStatus.RequiresServerSideVerification"/> and refuses. That is the
/// Owner's D2 requirement stated as behaviour: no assumption that process discipline alone is sufficient,
/// and no assumption that a protection exists because it should.
/// </para>
/// </summary>
public sealed class GitReleaseRefPolicy : IReleaseRefPolicy
{
    /// <summary>
    /// Ref names that count as release refs. A convention, named here rather than discovered, so that
    /// "there is no release ref" is a checkable statement rather than a matter of opinion.
    /// </summary>
    public static readonly IReadOnlyList<string> ReleaseRefNames = ["refs/heads/release", "refs/heads/releases"];

    private readonly IProcessRunner _runner;
    private readonly IReadOnlySet<string> _refsObservedBeforeAssessment;

    /// <param name="runner">Process runner, injectable so this is testable without git.</param>
    /// <param name="refsObservedBeforeAssessment">
    /// Every ref observed in the repository <b>before</b> the certification ran. A release ref absent from
    /// this set did not exist beforehand, so it was created for the assessment and is refused as
    /// fabricated. Required and non-empty: an empty set would make the fabrication check vacuous.
    /// </param>
    public GitReleaseRefPolicy(IProcessRunner runner, IReadOnlySet<string> refsObservedBeforeAssessment)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        ArgumentNullException.ThrowIfNull(refsObservedBeforeAssessment);

        if (refsObservedBeforeAssessment.Count == 0)
        {
            throw new ArgumentException(
                "The set of refs observed before the assessment is required. Without it, a ref created to pass this check cannot be told apart from one that already existed.",
                nameof(refsObservedBeforeAssessment));
        }

        _refsObservedBeforeAssessment = refsObservedBeforeAssessment;
    }

    /// <summary>Reads every ref in a repository, for the pre-assessment observation.</summary>
    public static async Task<IReadOnlySet<string>> ObserveRefsAsync(IProcessRunner runner, string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync("git", ["for-each-ref", "--format=%(refname)", "refs/heads"], repositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded
            ? result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    public async Task<ReleaseRefAssessment> AssessAsync(
        string repositoryLabel,
        string repositoryRoot,
        string buildCommitSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildCommitSha);

        var refs = await ObserveRefsAsync(_runner, repositoryRoot, cancellationToken).ConfigureAwait(false);
        var releaseRef = ReleaseRefNames.FirstOrDefault(refs.Contains);

        if (releaseRef is null)
        {
            var reported = refs.Where(r => r.Contains("release", StringComparison.OrdinalIgnoreCase)).ToList();

            return ReleaseRefAssessment.Ineligible(
                new ReleaseRefDescriptor(
                    repositoryLabel,
                    "(none)",
                    ReleaseRefProtectionStatus.Absent,
                    ObservedBeforeCertification: true,
                    ProtectionMechanism: null),
                [ReleaseRefRefusalReason.NoReleaseRefExists],
                [
                    $"'{repositoryLabel}' has no ref at any of: {string.Join(", ", ReleaseRefNames)}.",
                    reported.Count == 0
                        ? "No ref containing 'release' exists either."
                        : $"Refs containing 'release' that do not qualify: {string.Join(", ", reported)}."
                ]);
        }

        var observedBefore = _refsObservedBeforeAssessment.Contains(releaseRef);

        var tipResult = await _runner.RunAsync("git", ["rev-parse", releaseRef], repositoryRoot, cancellationToken).ConfigureAwait(false);
        var tip = tipResult.Succeeded ? tipResult.StandardOutput.Trim() : null;

        var descriptor = new ReleaseRefDescriptor(
            repositoryLabel,
            releaseRef,
            ReleaseRefProtectionStatus.RequiresServerSideVerification,
            observedBefore,
            tip,
            ProtectionMechanism: null);

        if (!observedBefore)
        {
            return ReleaseRefAssessment.Ineligible(
                descriptor,
                [ReleaseRefRefusalReason.FabricatedRefNotPermitted],
                [
                    $"'{releaseRef}' in '{repositoryLabel}' did not exist before this assessment. A ref created to satisfy the check is a naming convention, not governance."
                ]);
        }

        // Does the ref actually contain the commit this build was made from? A release ref pointing
        // somewhere else is not this build's governed source.
        var containsResult = await _runner.RunAsync("git", ["merge-base", "--is-ancestor", buildCommitSha, releaseRef], repositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        if (!containsResult.Succeeded)
        {
            return ReleaseRefAssessment.Ineligible(
                descriptor,
                [ReleaseRefRefusalReason.RefDoesNotContainBuildCommit],
                [$"'{releaseRef}' does not contain {buildCommitSha[..Math.Min(12, buildCommitSha.Length)]}."]);
        }

        // Local protection cannot be established by a checkout either, and server-side protection is
        // explicitly not assumed. Both halves are unreported, so the assessment refuses rather than
        // declaring a protection it did not observe.
        return ReleaseRefAssessment.Ineligible(
            descriptor,
            [ReleaseRefRefusalReason.ServerSideProtectionUnverified, ReleaseRefRefusalReason.RefNotProtected],
            [
                $"'{releaseRef}' exists and contains the build commit, but no protection could be verified.",
                "Local/pipeline refusal is not installed for this ref, and server-side ruleset protection cannot be observed from a checkout.",
                "Per the Owner's D2 ruling this is refused rather than assumed (DEPLOYMENT_GOVERNANCE_MODEL.md 4)."
            ]);
    }
}
