using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// TASK 2 — <b>the governed reproducible-build source-root contract.</b>
///
/// <para>
/// W9.5 built the same source twice from two different absolute paths and got <c>Divergent</c>. The product
/// was not the cause: the roots were <c>git archive</c> extractions with no Git metadata, so the SDK could not
/// map source paths to a stable prefix and absolute paths reached the PDB. Re-running from real clones made
/// the same two paths byte-identical.
/// </para>
///
/// <para>
/// <b>The refusal that existed was about the output; the condition that failed was about the input.</b> These
/// tests pin the condition, and each refusal is paired with the admitted case so a verifier hard-wired to
/// refuse fails the suite.
/// </para>
/// </summary>
public sealed class W96SourceRootProvenanceTests
{
    /// <summary>
    /// A runner that answers Git's provenance questions from a scripted description of one root, so each test
    /// varies exactly one condition.
    /// </summary>
    private sealed class ScriptedGitRunner : IProcessRunner
    {
        private readonly bool _isWorkTree;
        private readonly bool _revParseSucceeds;
        private readonly string _head;
        private readonly string _status;
        private readonly bool _repositoryHoldsCommit;
        private readonly string? _topLevel;

        internal ScriptedGitRunner(
            bool isWorkTree = true,
            bool revParseSucceeds = true,
            string head = "2402503886155f800809540841427cfc345dc06d",
            string status = "",
            bool repositoryHoldsCommit = true,
            string? topLevel = null)
        {
            _isWorkTree = isWorkTree;
            _revParseSucceeds = revParseSucceeds;
            _head = head;
            _status = status;
            _repositoryHoldsCommit = repositoryHoldsCommit;
            _topLevel = topLevel;
        }

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken = default)
        {
            var verb = arguments.Count > 0 ? arguments[0] : string.Empty;

            return Task.FromResult(verb switch
            {
                "rev-parse" when arguments.Contains("--is-inside-work-tree") =>
                    _isWorkTree
                        ? new ProcessResult(0, "true", string.Empty)
                        : new ProcessResult(128, string.Empty, "fatal: not a git repository"),

                // A root that is its own work tree reports itself as the top level; one that has inherited an
                // ancestor's identity reports the ancestor.
                "rev-parse" when arguments.Contains("--show-toplevel") =>
                    new ProcessResult(0, _topLevel ?? Root, string.Empty),

                "rev-parse" =>
                    _revParseSucceeds
                        ? new ProcessResult(0, _head, string.Empty)
                        : new ProcessResult(128, string.Empty, "fatal: ambiguous argument 'HEAD'"),

                "status" => new ProcessResult(0, _status, string.Empty),

                "cat-file" => _repositoryHoldsCommit
                    ? new ProcessResult(0, string.Empty, string.Empty)
                    : new ProcessResult(1, string.Empty, "fatal: Not a valid object name"),

                _ => new ProcessResult(1, string.Empty, $"unexpected verb '{verb}'")
            });
        }
    }

    private const string Commit = "2402503886155f800809540841427cfc345dc06d";
    private const string Root = @"D:\build\src-a";
    private const string Repository = @"D:\Nexus\PRT\MarketSurvey";

    private static Task<SourceRootProvenanceVerdict> Verify(IProcessRunner runner)
        => new SourceRootProvenanceVerifier(runner).VerifyAsync(Root, Repository, Commit);

    // =============================================================================================
    // The admitted case — without it, every refusal below is vacuous
    // =============================================================================================

    [Fact]
    public async Task AGitWorkTreeAtTheDeclaredCommitIsAdmitted()
    {
        var verdict = await Verify(new ScriptedGitRunner());

        Assert.True(verdict.IsAdmitted, verdict.Detail);
        Assert.Equal(SourceRootRefusalReason.None, verdict.RefusalReason);
        Assert.True(verdict.IsGitWorkTree);
        Assert.False(verdict.IsDirty);
        Assert.True(verdict.ResolvableInDeclaredRepository);
    }

    // =============================================================================================
    // The W9.5 case
    // =============================================================================================

    /// <summary>
    /// <b>THE W9.5 CASE.</b> A root with no Git metadata is refused as <c>SOURCE_PROVENANCE_UNVERIFIABLE</c> —
    /// not as a digest divergence, and not admitted to the build at all.
    /// </summary>
    [Fact]
    public async Task ARootWithNoGitMetadataIsRefusedAsUnverifiableProvenance()
    {
        var verdict = await Verify(new ScriptedGitRunner(isWorkTree: false));

        Assert.False(verdict.IsAdmitted);
        Assert.Equal(SourceRootRefusalReason.SourceProvenanceUnverifiable, verdict.RefusalReason);
        Assert.False(verdict.IsGitWorkTree);

        // The refusal must NAME THE INPUT CONDITION, and the reason code is what a caller branches on — so the
        // assertion is on the code, not on the prose. The detail mentions divergence deliberately: it explains
        // what the reader would otherwise have been shown instead, which is the whole point of this vocabulary.
        // Asserting the word is absent would forbid the explanation that makes the refusal useful.
        Assert.Contains("Git work tree", verdict.Detail, StringComparison.Ordinal);
        Assert.Contains("provenance cannot be established", verdict.Detail, StringComparison.Ordinal);

        // And the detail says which condition failed rather than merely that something did.
        Assert.Contains("no Git work tree", verdict.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The ancestor case, found by running W9.6's own control.</b> Git searches upward, so a copied
    /// directory beneath an unrelated repository reports <i>that</i> repository's work tree and HEAD. This is
    /// worse than carrying no metadata: the build would acquire a provenance that resolves to the wrong
    /// repository, and the divergence that followed would look like a product defect.
    /// </summary>
    [Fact]
    public async Task ARootInsideAnUnrelatedWorkTreeIsRefused()
    {
        var verdict = await Verify(new ScriptedGitRunner(topLevel: @"D:\NEXUS_V3_AUDIT"));

        Assert.False(verdict.IsAdmitted);
        Assert.Equal(SourceRootRefusalReason.SourceRootIsInsideAnUnrelatedWorkTree, verdict.RefusalReason);

        // The refusal must name the ancestor, so a reader can see WHICH repository the root inherited. The
        // verifier normalises separators before comparing, and reports the normalised form.
        Assert.Contains("D:/NEXUS_V3_AUDIT", verdict.Detail, StringComparison.Ordinal);

        // And it must be distinguishable from the absence case: both mean "not admitted", and they send a
        // reader to different places.
        Assert.NotEqual(SourceRootRefusalReason.SourceProvenanceUnverifiable, verdict.RefusalReason);
    }

    /// <summary>A root that is a work tree but whose HEAD cannot be read is a probe failure, not an absence.</summary>
    [Fact]
    public async Task AWorkTreeWhoseHeadCannotBeReadReportsAProbeFailure()
    {
        var verdict = await Verify(new ScriptedGitRunner(revParseSucceeds: false));

        Assert.False(verdict.IsAdmitted);
        Assert.Equal(SourceRootRefusalReason.ProvenanceProbeFailed, verdict.RefusalReason);

        // Distinct from absence on purpose: absence is a fact about the root, this is a failure to establish
        // one, and a reader acts differently on each.
        Assert.NotEqual(SourceRootRefusalReason.SourceProvenanceUnverifiable, verdict.RefusalReason);
    }

    [Fact]
    public async Task ARootAtADifferentCommitIsRefused()
    {
        var verdict = await Verify(new ScriptedGitRunner(head: "0000000000000000000000000000000000000000"));

        Assert.False(verdict.IsAdmitted);
        Assert.Equal(SourceRootRefusalReason.SourceRootCommitMismatch, verdict.RefusalReason);
    }

    [Fact]
    public async Task ADirtyRootIsRefused()
    {
        var verdict = await Verify(new ScriptedGitRunner(status: " M apps/api/Program.cs"));

        Assert.False(verdict.IsAdmitted);
        Assert.Equal(SourceRootRefusalReason.SourceRootIsDirty, verdict.RefusalReason);
        Assert.True(verdict.IsDirty);
    }

    /// <summary>
    /// A clean work tree at the right commit, in a repository that does not hold that commit, is refused: the
    /// root's provenance would not resolve to the repository being released.
    /// </summary>
    [Fact]
    public async Task ARootWhoseCommitTheDeclaredRepositoryDoesNotHoldIsRefused()
    {
        var verdict = await Verify(new ScriptedGitRunner(repositoryHoldsCommit: false));

        Assert.False(verdict.IsAdmitted);
        Assert.Equal(SourceRootRefusalReason.SourceRootNotResolvableInDeclaredRepository, verdict.RefusalReason);
        Assert.False(verdict.ResolvableInDeclaredRepository);
    }

    // =============================================================================================
    // The verdict is about the input, and it is complete
    // =============================================================================================

    /// <summary>
    /// Every refusal carries the observations it was made from, so a reader can see which condition failed
    /// rather than inferring it from a sentence.
    /// </summary>
    [Fact]
    public async Task EveryVerdictReportsTheConditionsItObserved()
    {
        var admitted = await Verify(new ScriptedGitRunner());
        var refused = await Verify(new ScriptedGitRunner(isWorkTree: false));

        Assert.Equal(Commit, admitted.Commit);
        Assert.Equal(Root, admitted.Root);
        Assert.Equal(Root, refused.Root);
        Assert.Null(refused.Commit);
        Assert.False(refused.ResolvableInDeclaredRepository);
    }
}
