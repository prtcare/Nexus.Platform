namespace Nexus.Delivery.Contracts;

/// <summary>
/// The governed source a release was built from.
///
/// <para>
/// <see cref="WorkingTreeIsDirty"/> is not informational. A bundle built from a dirty tree cannot
/// name the source it came from, so the promotion guarantee — "the deployed bytes came from
/// governed source" — is void for it. The state machine refuses it rather than recording a caveat,
/// because a caveat in this position is indistinguishable from a hole.
/// </para>
///
/// <para>
/// W9.0 measured the reason this matters: 82 worktrees and 428 uncommitted changes across nine
/// repositories, and no repository on a release branch. "Build from source" did not identify a
/// tree at all until this contract named one.
/// </para>
/// </summary>
public sealed record ReleaseSource
{
    public ReleaseSource(string commitSha, string refName, bool workingTreeIsDirty)
    {
        if (string.IsNullOrWhiteSpace(commitSha))
        {
            throw new ArgumentException("A release source must name a commit.", nameof(commitSha));
        }

        if (string.IsNullOrWhiteSpace(refName))
        {
            throw new ArgumentException("A release source must name the ref it was built from.", nameof(refName));
        }

        CommitSha = commitSha;
        RefName = refName;
        WorkingTreeIsDirty = workingTreeIsDirty;
    }

    public string CommitSha { get; }

    /// <summary>The ref the commit was reached through — the ref that governed authority protects.</summary>
    public string RefName { get; }

    public bool WorkingTreeIsDirty { get; }
}
