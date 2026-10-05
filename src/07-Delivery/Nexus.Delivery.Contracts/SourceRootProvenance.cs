using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// Why a build source root was not admitted to a certified, reproducible build.
///
/// <para>
/// <b>Why these are typed reasons rather than a digest comparison.</b> W9.5 built the remediated release twice
/// from two source roots at deliberately different absolute paths and got <c>Divergent</c> — a 126-byte
/// difference in the DLL and a 48,466-byte difference in the PDB. The causes were not the product. The roots
/// had been produced with <c>git archive</c>, so <b>neither had a <c>.git</c> directory</b>, and without a
/// repository root the SDK cannot map source paths to a stable prefix — so absolute paths reached the PDB and
/// the two builds disagreed. Re-running with real clones produced <c>ByteIdentical</c> from the same two
/// paths.
/// </para>
///
/// <para>
/// <b>The failure mode is the finding.</b> A missing repository identity announced itself as a digest
/// divergence, which is a statement about the <i>output</i> — so the natural next move is to go looking at the
/// product. The condition that actually failed is a statement about the <i>input</i>, and it was checkable
/// before the first compiler ran. These reasons exist so that the refusal names the input condition instead.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceRootRefusalReason
{
    None = 0,

    /// <summary>
    /// The root carries no Git metadata at all — an extracted archive, a copied directory, a build tree that
    /// was tarred and unpacked. <b>This is the W9.5 case and it is the reason the vocabulary exists.</b>
    /// </summary>
    SourceProvenanceUnverifiable,

    /// <summary>The root is inside a Git work tree but the command could not be run — a missing git binary, a
    /// broken repository, a permission failure. Distinct from absence: absence is a fact, this is a failure to
    /// establish one.</summary>
    ProvenanceProbeFailed,

    /// <summary>
    /// <b>The root is inside a work tree that belongs to a different repository.</b> Git searches upward, so a
    /// plain copied directory placed anywhere beneath an unrelated repository answers
    /// <c>--is-inside-work-tree</c> with <c>true</c> and reports <i>that</i> repository's root and HEAD.
    ///
    /// <para>
    /// This is more dangerous than carrying no metadata at all, because the build acquires a repository
    /// identity — just not its own. Source paths would be mapped against the wrong root, and the divergence
    /// that follows would be attributed to the product. Found while running W9.6's own negative control: the
    /// archive root was extracted under <c>D:\NEXUS_V3_AUDIT</c>, which is itself a Git repository, so the
    /// check that was meant to prove "no metadata" instead reported a commit mismatch against an audit
    /// repository the build has nothing to do with.
    /// </para>
    /// </summary>
    SourceRootIsInsideAnUnrelatedWorkTree,

    /// <summary>The root is a Git checkout at a different commit than the one being built.</summary>
    SourceRootCommitMismatch,

    /// <summary>The root has uncommitted changes, so the bytes built would not be the bytes the commit names.</summary>
    SourceRootIsDirty,

    /// <summary>The declared governed repository does not contain the commit the root claims to be at, so the
    /// root's provenance does not resolve to the repository being released.</summary>
    SourceRootNotResolvableInDeclaredRepository
}

/// <summary>
/// The governed condition a reproducible build source root must satisfy, and the answer for one root.
///
/// <para>
/// <b>A certified build admits only roots that are Git checkouts of the declared repository at the declared
/// commit, clean.</b> A plain copied directory must not silently participate: it will usually still
/// <i>compile</i>, and that is exactly what makes it dangerous — the build succeeds, the artifact is produced,
/// and the only symptom is that two builds of the same source disagree, which reads as a product defect.
/// </para>
///
/// <para>
/// <b>This is an entry condition, not a post-hoc diagnosis.</b> It is evaluated before the first round runs,
/// so a root that cannot carry provenance never reaches the compiler. Refusing afterwards would mean a build
/// had already produced bytes from a source whose identity was never established.
/// </para>
///
/// <para>
/// <b>The <c>.git</c> directory is never part of the artifact.</b> Nothing here copies repository metadata
/// into the build output — the check reads the root's Git state and then the packaging takes only the
/// project's published files. A source root that is a Git checkout therefore does not make the artifact
/// larger or carry provenance into it; it makes the build's <i>path mapping</i> stable, which is the
/// property the reproducibility claim rests on.
/// </para>
/// </summary>
/// <param name="Root">The source root as declared.</param>
/// <param name="IsGitWorkTree">Whether the root identified itself as a Git work tree.</param>
/// <param name="Commit">The root's HEAD, when it could be read.</param>
/// <param name="IsDirty">Whether the root had uncommitted changes.</param>
/// <param name="ResolvableInDeclaredRepository">Whether the declared repository also holds that commit.</param>
/// <param name="IsAdmitted">True only when every condition held.</param>
/// <param name="RefusalReason">Why it was not admitted, when it was not.</param>
/// <param name="Detail">Operator-facing. Never a credential value.</param>
public sealed record SourceRootProvenanceVerdict(
    string Root,
    bool IsGitWorkTree,
    string? Commit,
    bool IsDirty,
    bool ResolvableInDeclaredRepository,
    bool IsAdmitted,
    SourceRootRefusalReason RefusalReason,
    string Detail)
{
    public static SourceRootProvenanceVerdict Admitted(string root, string commit)
        => new(root, true, commit, false, true, true, SourceRootRefusalReason.None,
            $"Git work tree at {commit}, clean and resolvable in the declared repository.");

    public static SourceRootProvenanceVerdict Refused(
        string root,
        bool isGitWorkTree,
        string? commit,
        bool isDirty,
        bool resolvable,
        SourceRootRefusalReason reason,
        string detail)
        => new(root, isGitWorkTree, commit, isDirty, resolvable, false, reason, detail);
}
