namespace Nexus.Delivery.Contracts;

/// <summary>
/// One governed source revision entering a build: which repository, which commit.
///
/// <para>
/// A <b>set</b> of these, not one, because a release is rarely built from one repository. W9.0 measured
/// nine repositories in this estate, four of which publish packages consumed by others; a build that
/// cannot name every revision it consumed cannot be reproduced, and a reproducibility claim that names
/// only the "main" repository is a claim about a different build.
/// </para>
///
/// <para>
/// The commit is required to be a full hexadecimal SHA. A short SHA or a branch name is refused, because
/// neither is a fixed input: a branch moves, and a short SHA is ambiguous by construction.
/// </para>
/// </summary>
public sealed record SourceRevision
{
    public SourceRevision(string repositoryLabel, string commitSha, bool workingTreeIsDirty)
    {
        if (string.IsNullOrWhiteSpace(repositoryLabel))
        {
            throw new ArgumentException("A source revision must name its repository.", nameof(repositoryLabel));
        }

        if (!IsFullHexSha(commitSha))
        {
            throw new ArgumentException(
                "A source revision must carry a full hexadecimal commit SHA (40 or 64 characters). "
                + "A short SHA is ambiguous and a branch name moves, so neither is a fixed build input.",
                nameof(commitSha));
        }

        RepositoryLabel = repositoryLabel;
        CommitSha = commitSha.ToLowerInvariant();
        WorkingTreeIsDirty = workingTreeIsDirty;
    }

    public string RepositoryLabel { get; }

    public string CommitSha { get; }

    public bool WorkingTreeIsDirty { get; }

    /// <summary>Canonical form used when deriving a build id. Stable, lowercase, unambiguous.</summary>
    public override string ToString() => $"{RepositoryLabel}@{CommitSha}{(WorkingTreeIsDirty ? "+dirty" : string.Empty)}";

    internal static bool IsFullHexSha(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && (value.Length == 40 || value.Length == 64)
           && value.All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
}
