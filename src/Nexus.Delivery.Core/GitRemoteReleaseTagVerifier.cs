using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// Reads what the remote holds at a release reference name, using <c>git ls-remote</c>.
///
/// <para>
/// <b>Why <c>ls-remote</c> and not a fetch.</b> The question is "what is at that name on the remote", and
/// <c>ls-remote</c> answers it without writing to the repository, without moving <c>FETCH_HEAD</c>, and
/// without creating a ref. A verification that had to modify the local repository to observe the remote
/// would be a verification that changes the thing it is checking — and the deployment stage runs this
/// before every promotion, so it must be safe to run at any time, repeatedly, and concurrently.
/// </para>
///
/// <para>
/// <b>Two object ids, and the second one is not optional.</b> A single <c>ls-remote &lt;ref&gt;</c> prints
/// one line and stops. Measured against git 2.x, on a repository holding an annotated tag:
/// </para>
/// <code>
/// $ git ls-remote &lt;remote&gt; refs/tags/release/unit/1.0.0
/// 87fcc918…&#9;refs/tags/release/unit/1.0.0
/// </code>
/// <para>
/// That is the tag object, and it is <b>all</b> that is printed — the peeled commit does not appear. Asking
/// for the peel explicitly is what produces it, so both patterns are passed on the same command line:
/// </para>
/// <code>
/// $ git ls-remote &lt;remote&gt; refs/tags/release/unit/1.0.0 refs/tags/release/unit/1.0.0^{}
/// 87fcc918…&#9;refs/tags/release/unit/1.0.0
/// 1e14bf8e…&#9;refs/tags/release/unit/1.0.0^{}
/// </code>
/// <para>
/// The two lines are the tag object and its target, and <b>the presence of the second line is the
/// annotated/lightweight distinction on the remote</b>: a lightweight tag resolves straight to a commit, so
/// git peels nothing and prints one line. This was measured against a throwaway repository holding one tag
/// of each kind rather than inferred, because the difference between "one line" and "two lines" is the whole
/// check. It is also the same class of defect the first real W9.3 run hit — a mechanism that agreed with its
/// fake and disagreed with git — so the shape below is the measured shape, not the plausible one.
/// </para>
///
/// <para>
/// <b>An absent ref and an unreachable remote are read differently on purpose.</b> An absent ref is an
/// empty result <b>and exit 0</b>; an unreachable remote is a non-zero exit with a diagnostic on stderr.
/// Only the exit code separates them, so the exit code is what decides.
/// </para>
/// </summary>
public sealed class GitRemoteReleaseTagVerifier : IRemoteReleaseTagVerifier
{
    private readonly IProcessRunner _runner;

    /// <param name="runner">Process runner, injectable so this is testable without git and without a network.</param>
    public GitRemoteReleaseTagVerifier(IProcessRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<RemoteReleaseTagObservation> ObserveAsync(
        string remote,
        string repositoryRoot,
        ReleaseRecord release,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(release);

        // The ref name comes from the policy and from nowhere else. A second spelling of the release
        // reference here would be the drift this estate has recorded four times: two places deciding one
        // name, disagreeing only on the run where it matters.
        var refName = IReleaseTagPolicy.RefNameFor(release);
        var peeledName = refName + "^{}";

        var result = await _runner
            .RunAsync("git", ["ls-remote", remote, refName, peeledName], repositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return RemoteReleaseTagObservation.Unreachable(remote, refName, FirstLine(result.StandardError));
        }

        string? tagObjectSha = null;
        string? targetCommitSha = null;

        foreach (var line in result.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            // "<sha>\t<refname>". A tab is git's separator; a line without one is not a ref listing and is
            // skipped rather than parsed leniently, because a lenient parse of an unknown shape is how a
            // future git change becomes a silent pass.
            var separator = trimmed.IndexOf('\t', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var sha = trimmed[..separator];
            var name = trimmed[(separator + 1)..];

            if (string.Equals(name, refName, StringComparison.Ordinal))
            {
                tagObjectSha = sha;
            }
            else if (string.Equals(name, peeledName, StringComparison.Ordinal))
            {
                targetCommitSha = sha;
            }
        }

        if (tagObjectSha is null)
        {
            // The remote answered and printed nothing for this name. Exit 0 with no matching line is
            // "absent", and it is reported as its own state rather than as a failure to read.
            return RemoteReleaseTagObservation.Absent(remote, refName);
        }

        return targetCommitSha is null
            ? RemoteReleaseTagObservation.Lightweight(remote, refName, tagObjectSha)
            : RemoteReleaseTagObservation.Annotated(remote, refName, tagObjectSha, targetCommitSha);
    }

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(the remote produced no diagnostic)";
        }

        var line = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0].Trim();
        return line.Length == 0 ? "(the remote produced no diagnostic)" : line;
    }
}
