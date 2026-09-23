using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// A minimal in-memory git, so the release-reference mechanism is testable without a repository.
///
/// <para>
/// It answers only the commands the mechanism issues, and it distinguishes an annotated tag (which git
/// reports as object type <c>tag</c>) from a lightweight one (which it reports as <c>commit</c>), because
/// that single word is the whole annotated/lightweight distinction the policy reads.
/// </para>
/// </summary>
internal sealed class FakeGit : IProcessRunner
{
    private readonly Dictionary<string, string> _annotatedMessages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _annotatedTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lightweightTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _branchTargets = new(StringComparer.Ordinal);

    public int TagCreationCount { get; private set; }

    public void WriteAnnotatedTag(string refName, string commitSha, string message)
    {
        _annotatedMessages[refName] = message;
        _annotatedTargets[refName] = commitSha;
    }

    public void WriteLightweightTag(string refName, string commitSha) => _lightweightTargets[refName] = commitSha;

    public void WriteBranch(string refName, string commitSha) => _branchTargets[refName] = commitSha;

    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var command = arguments.Count > 0 ? arguments[0] : string.Empty;

        return Task.FromResult(command switch
        {
            "cat-file" => CatFile(arguments),
            "rev-parse" => RevParse(arguments),
            "rev-list" => RevList(arguments),
            "for-each-ref" => ForEachRef(arguments),
            "tag" => Tag(arguments),
            "merge-base" => new ProcessResult(1, string.Empty, string.Empty),
            _ => new ProcessResult(129, string.Empty, $"unsupported: {command}")
        });
    }

    private ProcessResult CatFile(IReadOnlyList<string> arguments)
    {
        // git cat-file -t <ref>
        var refName = arguments[^1];

        if (_annotatedMessages.ContainsKey(refName))
        {
            return new ProcessResult(0, "tag", string.Empty);
        }

        if (_lightweightTargets.ContainsKey(refName) || _branchTargets.ContainsKey(refName))
        {
            return new ProcessResult(0, "commit", string.Empty);
        }

        return new ProcessResult(128, string.Empty, "fatal: Not a valid object name");
    }

    private ProcessResult RevParse(IReadOnlyList<string> arguments)
    {
        var refName = arguments[^1];

        // rev-parse --verify --quiet exists to ask only "does it resolve?"
        var quiet = arguments.Contains("--verify");

        if (_annotatedMessages.ContainsKey(refName))
        {
            return new ProcessResult(0, TagObjectSha(refName), string.Empty);
        }

        if (_lightweightTargets.TryGetValue(refName, out var lightweight))
        {
            return new ProcessResult(0, lightweight, string.Empty);
        }

        if (_branchTargets.TryGetValue(refName, out var branch))
        {
            return new ProcessResult(0, branch, string.Empty);
        }

        return quiet
            ? new ProcessResult(1, string.Empty, string.Empty)
            : new ProcessResult(128, string.Empty, "fatal: ambiguous argument");
    }

    private ProcessResult RevList(IReadOnlyList<string> arguments)
    {
        var refName = arguments[^1];

        if (_annotatedTargets.TryGetValue(refName, out var annotated))
        {
            return new ProcessResult(0, annotated, string.Empty);
        }

        if (_lightweightTargets.TryGetValue(refName, out var lightweight))
        {
            return new ProcessResult(0, lightweight, string.Empty);
        }

        if (_branchTargets.TryGetValue(refName, out var branch))
        {
            return new ProcessResult(0, branch, string.Empty);
        }

        return new ProcessResult(128, string.Empty, "fatal: unknown revision");
    }

    private ProcessResult ForEachRef(IReadOnlyList<string> arguments)
    {
        var refName = arguments[^1];

        return _annotatedMessages.TryGetValue(refName, out var message)
            ? new ProcessResult(0, message, string.Empty)
            : new ProcessResult(0, string.Empty, string.Empty);
    }

    private ProcessResult Tag(IReadOnlyList<string> arguments)
    {
        // git tag -a <name> <commit> -m <message>
        var name = arguments[2];
        var commit = arguments[3];
        var messageIndex = arguments.ToList().IndexOf("-m");
        var message = messageIndex >= 0 ? arguments[messageIndex + 1] : string.Empty;

        // git resolves <name> relative to refs/tags/, ALWAYS. A caller that passes the full ref therefore
        // creates refs/tags/refs/tags/... — and this fake models that, rather than storing whatever it was
        // handed.
        //
        // That faithfulness is the point. The first version of this fake stored the argument verbatim, so
        // it agreed with a publisher that passed the full ref, and the pair passed their tests while the
        // real mechanism created a tag at the wrong ref. A fake written to match the implementation can
        // only confirm the implementation's assumptions; a fake written to match the SYSTEM it stands in
        // for can contradict them. This one now can.
        var fullRef = "refs/tags/" + name;

        TagCreationCount++;
        WriteAnnotatedTag(fullRef, commit, message);

        return new ProcessResult(0, string.Empty, string.Empty);
    }

    /// <summary>A stable, obviously-fake tag object id derived from the ref name, so assertions can name it.</summary>
    private static string TagObjectSha(string refName)
    {
        var hash = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(refName));
        return Convert.ToHexStringLower(hash);
    }
}

/// <summary>
/// The governed release reference: that a correct source is accepted, and that every substitute a
/// reasonable person might reach for is refused with a typed reason.
/// </summary>
public sealed class ReleaseTagTests
{
    private const string RepositoryLabel = "PRT/MarketSurvey";

    private static ReleaseRecord Release(char digestFill = 'a')
        => ReleaseTestData.Record(identity: ReleaseTestData.Identity(digestFill: digestFill));

    [Fact]
    public void RefNameFor_FollowsTheOwnerRuling_AndLivesInTheTagNamespace()
    {
        var release = Release();

        Assert.Equal("refs/tags/release/marketsurvey.api/0.1.0", IReleaseTagPolicy.RefNameFor(release));
        Assert.Equal(IReleaseTagPolicy.RefNameFor(release), release.Identity.ReleaseRefName);
    }

    [Fact]
    public void Assess_AcceptsAGovernedAnnotatedTagAtTheCertifiedSource()
    {
        var release = Release();
        var git = new FakeGit();
        git.WriteAnnotatedTag(IReleaseTagPolicy.RefNameFor(release), BuildTestData.CommitA, ReleaseTagAnnotation.Format(release));

        var policy = new GitReleaseTagPolicy(git);

        var assessment = policy.AssessAsync(RepositoryLabel, ".", release).GetAwaiter().GetResult();

        // Eligible under a verified remote ruleset; the default constructor reports the server-side half as
        // unverified, which the next test asserts. Here the remote half is supplied because the question is
        // whether the REFERENCE itself is accepted.
        var verified = new GitReleaseTagPolicy(git, serverSideProtectionVerified: true)
            .AssessAsync(RepositoryLabel, ".", release).GetAwaiter().GetResult();

        Assert.True(verified.IsEligible);
        Assert.True(verified.IsGoverned);
        Assert.Equal(release.ReleaseId, verified.Descriptor!.AnnotatedReleaseId);
        Assert.True(assessment.IsGoverned);
        Assert.True(assessment.IsGovernedLocallyButUnprotectedRemotely);
        Assert.True(assessment.RefusedBecause(ReleaseTagRefusalReason.ServerSideProtectionUnverified));
    }

    /// <summary>
    /// The Owner's D2 requirement stated as behaviour: a local process cannot observe a remote ruleset, so
    /// the assessment reports what it did not verify instead of assuming it.
    /// </summary>
    [Fact]
    public void Assess_DoesNotInferServerSideProtection()
    {
        var release = Release();
        var git = new FakeGit();
        git.WriteAnnotatedTag(IReleaseTagPolicy.RefNameFor(release), BuildTestData.CommitA, ReleaseTagAnnotation.Format(release));

        var assessment = new GitReleaseTagPolicy(git).AssessAsync(RepositoryLabel, ".", release).GetAwaiter().GetResult();

        Assert.False(assessment.IsEligible);
        Assert.True(assessment.RefusedBecause(ReleaseTagRefusalReason.ServerSideProtectionUnverified));
        Assert.Equal(ReleaseRefProtectionStatus.RequiresServerSideVerification, assessment.Descriptor!.ProtectionStatus);
    }

    [Fact]
    public void Assess_RefusesATagPointingAtDifferentSource()
    {
        var release = Release();
        var git = new FakeGit();
        git.WriteAnnotatedTag(IReleaseTagPolicy.RefNameFor(release), BuildTestData.CommitB, ReleaseTagAnnotation.Format(release));

        var assessment = new GitReleaseTagPolicy(git).AssessAsync(RepositoryLabel, ".", release).GetAwaiter().GetResult();

        Assert.False(assessment.IsGoverned);
        Assert.True(assessment.RefusedBecause(ReleaseTagRefusalReason.TagDoesNotContainBuildCommit));
    }

    /// <summary>
    /// A tag whose annotation names a different release is not this release's reference, however correctly
    /// it is named. This is what makes the governed block load-bearing rather than decorative.
    /// </summary>
    [Fact]
    public void Assess_RefusesATagIdentifiesADifferentRelease()
    {
        var release = Release();
        var other = Release(digestFill: 'b');
        var git = new FakeGit();
        git.WriteAnnotatedTag(IReleaseTagPolicy.RefNameFor(release), BuildTestData.CommitA, ReleaseTagAnnotation.Format(other));

        var assessment = new GitReleaseTagPolicy(git).AssessAsync(RepositoryLabel, ".", release).GetAwaiter().GetResult();

        Assert.False(assessment.IsGoverned);
        Assert.True(assessment.RefusedBecause(ReleaseTagRefusalReason.TagIdentifiesDifferentRelease));
    }

    /// <summary>
    /// A moved tag: recreated at the right commit with an annotation rebuilt from the same identity, so
    /// every field the release id is derived from agrees. Only the record digest can catch it, which is why
    /// the annotation carries one.
    /// </summary>
    [Fact]
    public void Assess_RefusesAMovedTag_EvenWhenTheReleaseIdStillMatches()
    {
        var release = Release();

        // Same identity, different non-identity metadata: the release id is unchanged, the record differs.
        // Built as a second record rather than mutated, because ReleaseRecord has no settable member and
        // that is the point of it.
        var moved = ReleaseTestData.Record(configurationSchemaVersion: "runtime-config-v2");
        Assert.Equal(release.ReleaseId, moved.ReleaseId);
        Assert.NotEqual(release.ComputeRecordDigest(), moved.ComputeRecordDigest());

        var git = new FakeGit();
        git.WriteAnnotatedTag(IReleaseTagPolicy.RefNameFor(release), BuildTestData.CommitA, ReleaseTagAnnotation.Format(moved));

        var assessment = new GitReleaseTagPolicy(git).AssessAsync(RepositoryLabel, ".", release).GetAwaiter().GetResult();

        Assert.False(assessment.IsGoverned);
        Assert.True(assessment.RefusedBecause(ReleaseTagRefusalReason.TagWasMoved));
        Assert.Contains("re-pointed", string.Join(" ", assessment.Detail), StringComparison.Ordinal);
    }

    /// <summary>
    /// A lightweight tag carries no message, so it cannot carry the governed identity and cannot be verified
    /// against the release it claims to name. Refused rather than upgraded, because upgrading would mean
    /// writing the governed annotation after the fact.
    /// </summary>
    [Fact]
    public void Assess_RefusesALightweightTag()
    {
        var release = Release();
        var git = new FakeGit();
        git.WriteLightweightTag(IReleaseTagPolicy.RefNameFor(release), BuildTestData.CommitA);

        var assessment = new GitReleaseTagPolicy(git).AssessAsync(RepositoryLabel, ".", release).GetAwaiter().GetResult();

        Assert.False(assessment.IsGoverned);
        Assert.True(assessment.RefusedBecause(ReleaseTagRefusalReason.LightweightTagNotPermitted));
        Assert.False(assessment.Descriptor!.IsAnnotated);
    }

    /// <summary>
    /// The substitution the temptation points at: a branch given the release reference's name, pointed at
    /// the right commit. Refused by name, before any ref is read.
    /// </summary>
    [Fact]
    public void AssessRef_RefusesABranchHeadAsAnImmutableReference()
    {
        var release = Release();
        var git = new FakeGit();
        const string branch = "refs/heads/release/marketsurvey.api/0.1.0";
        git.WriteBranch(branch, BuildTestData.CommitA);

        var assessment = new GitReleaseTagPolicy(git, serverSideProtectionVerified: true)
            .AssessRefAsync(RepositoryLabel, ".", branch, release).GetAwaiter().GetResult();

        Assert.False(assessment.IsEligible);
        Assert.True(assessment.RefusedBecause(ReleaseTagRefusalReason.BranchCannotSubstituteForImmutableTag));
    }

    /// <summary>
    /// A tag made by something other than the governed publisher: annotated, correctly named, pointing at
    /// the right commit, and carrying a message that is not a governed block.
    /// </summary>
    [Fact]
    public void Assess_RefusesAFabricatedRefThatCarriesNoGovernedBlock()
    {
        var release = Release();
        var git = new FakeGit();
        git.WriteAnnotatedTag(
            IReleaseTagPolicy.RefNameFor(release),
            BuildTestData.CommitA,
            $"prepared by hand for {release.UnitId.Value}");

        var assessment = new GitReleaseTagPolicy(git).AssessAsync(RepositoryLabel, ".", release).GetAwaiter().GetResult();

        Assert.False(assessment.IsGoverned);
        Assert.True(assessment.RefusedBecause(ReleaseTagRefusalReason.TagAnnotationNotGoverned));
    }

    [Fact]
    public void Assess_RefusesATagOutsideTheGovernedNamespace()
    {
        var release = Release();
        var git = new FakeGit();
        const string outside = "refs/tags/v0.1.0";
        git.WriteAnnotatedTag(outside, BuildTestData.CommitA, ReleaseTagAnnotation.Format(release));

        var assessment = new GitReleaseTagPolicy(git)
            .AssessRefAsync(RepositoryLabel, ".", outside, release).GetAwaiter().GetResult();

        Assert.True(assessment.RefusedBecause(ReleaseTagRefusalReason.OutsideReleaseTagNamespace));
    }

    [Fact]
    public void Assess_ReportsAbsenceAsATypedRefusal_NotAsAnException()
    {
        var release = Release();

        var assessment = new GitReleaseTagPolicy(new FakeGit())
            .AssessAsync(RepositoryLabel, ".", release).GetAwaiter().GetResult();

        Assert.False(assessment.IsEligible);
        Assert.True(assessment.RefusedBecause(ReleaseTagRefusalReason.TagAbsent));
        Assert.Equal(ReleaseRefProtectionStatus.Absent, assessment.Descriptor!.ProtectionStatus);
    }

    [Fact]
    public void Publish_CreatesAnAnnotatedTag_AndIsANoOpWhenAskedTwice()
    {
        var release = Release();
        var git = new FakeGit();
        var publisher = new GitReleaseTagPublisher(git, new GitReleaseTagPolicy(git));

        var first = publisher.PublishAsync(RepositoryLabel, ".", release, BuildTestData.CommitA).GetAwaiter().GetResult();

        Assert.True(first.IsCreated);
        Assert.Equal(1, git.TagCreationCount);

        var second = publisher.PublishAsync(RepositoryLabel, ".", release, BuildTestData.CommitA).GetAwaiter().GetResult();

        Assert.True(second.IsCreated);
        Assert.Equal(1, git.TagCreationCount);
        Assert.Equal(first.TagObjectSha, second.TagObjectSha);
    }

    [Fact]
    public void Publish_RefusesToMoveAnExistingReference()
    {
        var release = Release();
        var other = Release(digestFill: 'b');
        var refName = IReleaseTagPolicy.RefNameFor(release);

        var git = new FakeGit();
        var publisher = new GitReleaseTagPublisher(git, new GitReleaseTagPolicy(git));

        // A governed reference already exists for a DIFFERENT release.
        git.WriteAnnotatedTag(refName, BuildTestData.CommitA, ReleaseTagAnnotation.Format(other));

        var outcome = publisher.PublishAsync(RepositoryLabel, ".", release, BuildTestData.CommitA).GetAwaiter().GetResult();

        Assert.False(outcome.IsCreated);
        Assert.True(outcome.Created(ReleaseTagRefusalReason.TagIdentifiesDifferentRelease));
        Assert.Equal(0, git.TagCreationCount);
        Assert.Contains("never overwritten or moved", string.Join(" ", outcome.Detail), StringComparison.Ordinal);
    }

    /// <summary>
    /// The publisher must hand <c>git tag</c> the short name, because git resolves it relative to
    /// <c>refs/tags/</c>.
    ///
    /// <para>
    /// This test exists because the first real run created <c>refs/tags/refs/tags/release/...</c> while the
    /// whole suite was green. The publisher passed the full ref, the fake stored it verbatim, and the two
    /// agreed on an assumption neither had checked against git. The fake now models git's rule, so passing
    /// the full ref writes the tag where the assessor cannot find it — which this asserts.
    /// </para>
    /// </summary>
    [Fact]
    public void Publish_CreatesTheTagAtTheCanonicalRef_NotADoublePrefixedOne()
    {
        var release = Release();
        var git = new FakeGit();
        var publisher = new GitReleaseTagPublisher(git, new GitReleaseTagPolicy(git));

        var outcome = publisher.PublishAsync(RepositoryLabel, ".", release, BuildTestData.CommitA).GetAwaiter().GetResult();

        Assert.True(outcome.IsCreated);
        Assert.Equal(IReleaseTagPolicy.RefNameFor(release), outcome.RefName);
        Assert.Equal("refs/tags/release/marketsurvey.api/0.1.0", outcome.RefName);
        Assert.DoesNotContain("refs/tags/refs/tags", outcome.RefName, StringComparison.Ordinal);

        // And the reference is readable at the canonical name, which is the property that matters: a tag
        // written one level too deep is invisible to every assessor.
        var assessment = new GitReleaseTagPolicy(git, serverSideProtectionVerified: true)
            .AssessAsync(RepositoryLabel, ".", release).GetAwaiter().GetResult();

        Assert.True(assessment.IsGoverned);
    }

    [Fact]
    public void Publish_RefusesToTagACommitTheReleaseDoesNotCarry()
    {
        var release = Release();
        var git = new FakeGit();

        var outcome = new GitReleaseTagPublisher(git, new GitReleaseTagPolicy(git))
            .PublishAsync(RepositoryLabel, ".", release, BuildTestData.CommitB).GetAwaiter().GetResult();

        Assert.False(outcome.IsCreated);
        Assert.True(outcome.Created(ReleaseTagRefusalReason.TagDoesNotContainBuildCommit));
        Assert.Equal(0, git.TagCreationCount);
    }

    [Fact]
    public void Annotation_RoundTrips_AndRefusesAnythingItDoesNotFullyUnderstand()
    {
        var release = Release();
        var message = ReleaseTagAnnotation.Format(release);

        Assert.True(ReleaseTagAnnotation.TryParse(message, out var parsed));
        Assert.True(parsed!.Describes(release));
        Assert.Equal(release.ReleaseId, parsed.ReleaseId);
        Assert.Equal(release.Artifacts.Count, parsed.Artifacts.Count);

        // An unknown key is a refusal, not something to skip: a block this reader does not fully understand
        // is one it cannot claim to have verified.
        Assert.False(ReleaseTagAnnotation.TryParse(message + "futureField=whatever\n", out _));
        Assert.False(ReleaseTagAnnotation.TryParse("not a governed block", out _));
        Assert.False(ReleaseTagAnnotation.TryParse(null, out _));
    }

    [Fact]
    public void Annotation_DescribesARecordThatDiffersOnlyInNonIdentityMetadataAsDifferent()
    {
        var release = Release();
        var changed = ReleaseTestData.Record(configurationSchemaVersion: "runtime-config-v2");

        ReleaseTagAnnotation.TryParse(ReleaseTagAnnotation.Format(release), out var annotation);

        Assert.True(annotation!.Describes(release));
        Assert.False(annotation.Describes(changed));
    }
}
