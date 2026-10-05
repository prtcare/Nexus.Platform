namespace Nexus.Delivery.Contracts;

/// <summary>
/// The dependency lock state of a build — the part of "what went in" that a commit does not capture.
///
/// <para>
/// <b>Why this is required rather than optional.</b> A commit pins the source and says nothing about the
/// resolved dependency graph. Two builds of the same commit can consume different package versions when
/// a feed moves or a floating range resolves differently, and the resulting difference is invisible in
/// the commit. Recording the lock files' digests is what makes "identical governed inputs" a checkable
/// claim rather than an assumption.
/// </para>
///
/// <para>
/// <b>A build with no lock file is recorded as having none, not as being locked.</b>
/// <see cref="Unlocked"/> is a distinct and honest state. W9.0 measured that this estate's .NET
/// repositories restore from nuget.org by floating <c>PackageReference</c> versions with no
/// <c>packages.lock.json</c> anywhere — so for those units the honest record is "unlocked", and a
/// reproducibility claim about them must say so. Treating absence as locked would be exactly the kind of
/// false-green this estate keeps recording.
/// </para>
/// </summary>
public sealed record DependencyLockState
{
    private DependencyLockState(bool isLocked, IReadOnlyList<LockFileDigest> lockFiles, string? note)
    {
        IsLocked = isLocked;
        LockFiles = lockFiles;
        Note = note;
    }

    /// <summary>True only when at least one lock file was found and digested.</summary>
    public bool IsLocked { get; }

    /// <summary>Lock files, by repository-relative path, with their content digests.</summary>
    public IReadOnlyList<LockFileDigest> LockFiles { get; }

    /// <summary>Why the state is what it is. Required when unlocked.</summary>
    public string? Note { get; }

    /// <summary>A build whose dependency graph is pinned by at least one lock file.</summary>
    public static DependencyLockState Locked(IReadOnlyList<LockFileDigest> lockFiles)
    {
        ArgumentNullException.ThrowIfNull(lockFiles);

        if (lockFiles.Count == 0)
        {
            throw new ArgumentException(
                "A locked state must carry at least one lock file. Use DependencyLockState.Unlocked to record the absence honestly.",
                nameof(lockFiles));
        }

        return new DependencyLockState(true, [.. lockFiles], null);
    }

    /// <summary>
    /// A build with no lock file. <paramref name="note"/> is required and must say what was searched for,
    /// because "unlocked" without a reason is indistinguishable from a scan that did not run.
    /// </summary>
    public static DependencyLockState Unlocked(string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException(
                "An unlocked dependency state must state what was searched for. An unexplained absence is indistinguishable from a check that did not run.",
                nameof(note));
        }

        return new DependencyLockState(false, [], note);
    }

    public override string ToString() => IsLocked
        ? $"locked[{string.Join(",", LockFiles.Select(l => l.RelativePath))}]"
        : $"unlocked({Note})";
}

/// <summary>A lock file and its content digest. Path and digest only — lock files are not secret-bearing, but no file content is carried.</summary>
public sealed record LockFileDigest(string RepositoryLabel, string RelativePath, ArtifactDigest Digest);
