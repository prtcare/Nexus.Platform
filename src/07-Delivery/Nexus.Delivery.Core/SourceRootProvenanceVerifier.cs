using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// <b>Decides whether a build source root may participate in a certified, reproducible build</b>, before the
/// first compiler runs.
///
/// <para>
/// <b>The condition this establishes, and why it is checked here rather than inferred from a digest.</b> A
/// reproducible build from two different absolute paths is byte-identical only if the toolchain can map both
/// paths to a stable prefix, and the .NET SDK does that by locating the repository root. A source root with no
/// Git metadata has no repository root, so absolute paths reach the PDB and the two builds diverge — which is
/// what happened in W9.5, and what was reported as <c>Reproducibility is Divergent</c>, a statement about the
/// output. The input condition was the cause and it was checkable in advance.
/// </para>
///
/// <para>
/// <b>Four conditions, each answered by the root itself rather than by the plan.</b> The plan says what it
/// believes; this asks the working tree. A plan that named a commit the root is not at would otherwise be
/// recorded as the artifact's provenance, which is the one fact the whole certified chain rests on.
/// </para>
///
/// <list type="number">
/// <item><description>the root is a Git work tree;</description></item>
/// <item><description>its HEAD is the declared commit;</description></item>
/// <item><description>it is clean;</description></item>
/// <item><description>the declared governed repository also holds that commit, so the root's provenance
/// resolves to the repository being released.</description></item>
/// </list>
/// </summary>
public sealed class SourceRootProvenanceVerifier
{
    private readonly IProcessRunner _runner;

    public SourceRootProvenanceVerifier(IProcessRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <summary>
    /// Verifies one root against the declared repository and commit. Never throws for a refusal: every
    /// condition that can fail is a typed value on the verdict.
    /// </summary>
    /// <param name="root">The source root as the plan declares it.</param>
    /// <param name="declaredRepositoryRoot">The governed repository the build claims to be releasing.</param>
    /// <param name="declaredCommit">The commit the build claims to be building.</param>
    public async Task<SourceRootProvenanceVerdict> VerifyAsync(
        string root,
        string declaredRepositoryRoot,
        string declaredCommit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        // ---- 1. is there a work tree at all? -----------------------------------------------------
        // Asked before anything else, because every later question is unanswerable without it - and because
        // this is the condition whose absence produced a digest divergence rather than a provenance refusal.
        var insideWorkTree = await _runner
            .RunAsync("git", ["rev-parse", "--is-inside-work-tree"], root, cancellationToken)
            .ConfigureAwait(false);

        if (!insideWorkTree.Succeeded || !insideWorkTree.StandardOutput.Trim().Equals("true", StringComparison.Ordinal))
        {
            // A failed command and a negative answer are distinguished only in the detail: both mean the root
            // cannot carry provenance, and neither can be discovered later.
            return SourceRootProvenanceVerdict.Refused(
                root,
                isGitWorkTree: false,
                commit: null,
                isDirty: false,
                resolvable: false,
                SourceRootRefusalReason.SourceProvenanceUnverifiable,
                $"'{root}' carries no Git work tree, so its provenance cannot be established. A reproducible "
                + "build needs a repository root to map source paths to a stable prefix; without one the same "
                + "source at two different absolute paths produces two different binaries, and the divergence "
                + "is reported against the OUTPUT rather than against this input. Build from a clone or a "
                + "worktree, not from an archive or a copy.");
        }

        // ---- 1b. is that work tree the ROOT ITSELF, or an ancestor? ------------------------------
        // Git searches upward, so a copied directory placed anywhere beneath an unrelated repository answers
        // "yes, inside a work tree" and reports THAT repository's root. The build would then map source paths
        // against the wrong root and acquire a provenance that resolves to a repository it has nothing to do
        // with - which is worse than having no metadata, because the divergence that follows looks like a
        // product defect. This was found by running W9.6's own control: the archive root was extracted beneath
        // D:\NEXUS_V3_AUDIT, which is itself a Git repository.
        var topLevel = await _runner.RunAsync("git", ["rev-parse", "--show-toplevel"], root, cancellationToken).ConfigureAwait(false);

        if (topLevel.Succeeded)
        {
            var toplevel = topLevel.StandardOutput.Trim().Replace('\\', '/');
            var declared = root.Replace('\\', '/').TrimEnd('/');

            if (!string.Equals(toplevel.TrimEnd('/'), declared, StringComparison.OrdinalIgnoreCase))
            {
                return SourceRootProvenanceVerdict.Refused(
                    root, true, null, false, false,
                    SourceRootRefusalReason.SourceRootIsInsideAnUnrelatedWorkTree,
                    $"'{root}' is not itself a Git work tree - it is inside '{toplevel}'. A copied directory "
                    + "placed beneath an unrelated repository inherits that repository's identity and HEAD, so "
                    + "the build would map source paths against the wrong root and record provenance that "
                    + "resolves to a repository it has nothing to do with. Build from a clone or a worktree "
                    + "whose own top level is the root.");
            }
        }

        var head = await _runner.RunAsync("git", ["rev-parse", "HEAD"], root, cancellationToken).ConfigureAwait(false);

        if (!head.Succeeded)
        {
            return SourceRootProvenanceVerdict.Refused(
                root, true, null, false, false,
                SourceRootRefusalReason.ProvenanceProbeFailed,
                $"'{root}' is a Git work tree but its HEAD could not be read: {head.StandardError.Trim()}");
        }

        var commit = head.StandardOutput.Trim();

        // ---- 2. is it at the declared commit? ----------------------------------------------------
        if (!string.Equals(commit, declaredCommit, StringComparison.OrdinalIgnoreCase))
        {
            return SourceRootProvenanceVerdict.Refused(
                root, true, commit, false, false,
                SourceRootRefusalReason.SourceRootCommitMismatch,
                $"'{root}' is at {commit}, but the build declares {declaredCommit}. Building from a root at a "
                + "different commit would produce an artifact whose recorded provenance is not the source it "
                + "came from.");
        }

        // ---- 3. is it clean? ---------------------------------------------------------------------
        var status = await _runner.RunAsync("git", ["status", "--porcelain"], root, cancellationToken).ConfigureAwait(false);
        var isDirty = status.Succeeded && status.StandardOutput.Trim().Length > 0;

        if (isDirty)
        {
            return SourceRootProvenanceVerdict.Refused(
                root, true, commit, true, false,
                SourceRootRefusalReason.SourceRootIsDirty,
                $"'{root}' has uncommitted changes, so the bytes built would not be the bytes {commit} names. A "
                + "dirty tree cannot say where its source came from.");
        }

        // ---- 4. does the declared repository hold that commit? ------------------------------------
        var repository = await _runner
            .RunAsync("git", ["cat-file", "-e", $"{declaredCommit}^{{commit}}"], declaredRepositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        if (!repository.Succeeded)
        {
            return SourceRootProvenanceVerdict.Refused(
                root, true, commit, false, false,
                SourceRootRefusalReason.SourceRootNotResolvableInDeclaredRepository,
                $"'{root}' is a clean Git work tree at {commit}, but the declared repository "
                + $"'{declaredRepositoryRoot}' does not hold that commit. The root's provenance does not resolve "
                + "to the repository being released, so the artifact could not be traced back to it.");
        }

        return SourceRootProvenanceVerdict.Admitted(root, commit);
    }
}
