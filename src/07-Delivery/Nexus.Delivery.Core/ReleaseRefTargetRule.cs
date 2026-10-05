using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The one place the rule <i>"a release reference may only point at a commit the release carries"</i> is
/// implemented.
///
/// <para>
/// <b>Why this exists as a type rather than a <c>Contains</c> call at each site.</b> Three rules of the
/// Owner-approved C-2 compensating control reduce to this one judgement: rule 2 at creation time ("the tag
/// target must equal the certified SourceCommit exactly"), and rules 5 and 7 at deployment time ("the remote
/// tag target must equal the Release Bundle SourceCommit", "any remote-tag drift returns
/// <c>RELEASE_REFERENCE_DRIFT</c>"). Those are two different callers answering the same question about two
/// different observations, which is precisely the shape this estate has recorded four times as its most
/// expensive defect class: W9.3 shipped two record digests, W9.2 shipped two "is this a value?" judgements,
/// W9.1 shipped two identifier-shape rules, and in every case the two copies agreed until the run where
/// they did not.
/// </para>
///
/// <para>
/// <b>Why membership rather than equality with a single commit.</b> A release's identity carries the source
/// commits of <i>every</i> repository its build read, while a release reference lives in exactly one
/// repository. The caller names the repository and the commit, so the checkable claim is "the reference
/// points at a commit this release was built from", not "the reference points at the only commit this
/// release has" — the latter is false the moment a release consumes two repositories, and a rule that is
/// false in general is one that gets relaxed at the first multi-repository release rather than fixed.
/// </para>
/// </summary>
public static class ReleaseRefTargetRule
{
    /// <summary>
    /// True when <paramref name="commitSha"/> is one of the commits the certified build recorded for this
    /// release.
    ///
    /// <para>
    /// Ordinal-ignore-case, because git object ids are hexadecimal and an id quoted from a build record may
    /// legitimately be upper case. The rest of the estate compares object ids ordinally; this one comparison
    /// is case-insensitive because the two values arrive from different producers — one from git, one from a
    /// hand-written build record — and refusing a correct release over the case of a hex digit would be a
    /// refusal nobody could act on.
    /// </para>
    /// </summary>
    public static bool PointsAtACommitTheReleaseCarries(ReleaseRecord release, string? commitSha)
    {
        ArgumentNullException.ThrowIfNull(release);

        return !string.IsNullOrWhiteSpace(commitSha)
               && release.Identity.SourceCommits.Contains(commitSha, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The refusal detail for a reference that points somewhere the release does not carry, naming both the
    /// observed commit and the commits the release does carry. A refusal an operator cannot act on is a
    /// refusal that gets worked around.
    /// </summary>
    public static string DescribeRefusal(ReleaseRecord release, string? observedCommitSha)
    {
        ArgumentNullException.ThrowIfNull(release);

        return $"{observedCommitSha ?? "(unreadable)"} is not among the commits the certified build recorded "
               + $"for {release.ReleaseId} ({string.Join(", ", release.Identity.SourceCommits)}).";
    }
}
