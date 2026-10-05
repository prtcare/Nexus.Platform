using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// The five mandatory negative controls of the Owner-approved C-2 compensating control, as executable tests.
///
/// <para>
/// <b>What these are for.</b> The control's whole claim is that a deployment does not trust the release
/// reference. A test that only showed a correct release deploying would be evidence for the opposite claim
/// as well — it would pass just as green if the gate always returned "verified". Each control below therefore
/// constructs the specific substitution a reasonable person (or a push-capable adversary) might reach for,
/// and asserts that the gate refuses it <i>with the state that names it</i>.
/// </para>
///
/// <para>
/// <b>Fixtures are disposable and isolated.</b> Artifact bytes live under a <see cref="TempRoot"/> that is
/// deleted when the test ends; git is the in-memory <see cref="FakeGit"/>; the remote is a dictionary inside
/// that fake. No test here reads or writes the live authority workbook, touches <c>D:\NEXUS_ARTIFACTS</c>,
/// or pushes anything anywhere — there is no code path in this file that can reach a network.
/// </para>
///
/// <para>
/// <b>The real implementations are used end to end.</b> <see cref="GitReleaseTagPolicy"/>,
/// <see cref="GitRemoteReleaseTagVerifier"/>, <see cref="FileArtifactStore"/> and the pre-deployment gate are
/// the production types; only the git process boundary and the filesystem root are substituted. A control
/// proven against doubles of the mechanism would prove the doubles.
/// </para>
/// </summary>
public sealed class PreDeploymentGateTests
{
    private const string RepositoryLabel = "PRT/MarketSurvey";
    private const string Remote = "origin";

    /// <summary>
    /// C-1 — <b>a wrong remote tag target is refused.</b>
    ///
    /// <para>
    /// The remote holds an annotated tag at the release reference's own name, pointing at a commit that is
    /// not the certified source. This is the substitution rule 5 exists for: everything about the reference
    /// looks right — the name, the namespace, the annotation — and the one thing that is wrong is where it
    /// points. A deployment that trusted the name would proceed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NegativeControl1_WrongRemoteTagTarget_IsRefusedAsReferenceDrift()
    {
        using var fixture = await Fixture.CreateAsync();

        // Arranged so that ONLY the target is wrong: the block is this release's own governed block.
        fixture.Git.PublishAnnotatedTagToRemote(
            fixture.RefName,
            BuildTestData.CommitB,
            ReleaseTagAnnotation.Format(fixture.Release));

        var verdict = await fixture.VerifyAsync();

        Assert.False(verdict.IsDeployable);
        Assert.Equal(ReleaseRefPreDeploymentState.ReferenceDrift, verdict.State);
        Assert.True(verdict.IsReferenceDrift);
        Assert.Equal(ReleaseRefPreDeploymentVerdict.ReleaseReferenceDriftVerdict, verdict.VerdictText);
        Assert.True(verdict.RefusedBecause(ReleaseRefusalReason.ReleaseReferenceDrift));

        // Rule 9: the observation is recorded even though it was refused. An observation kept only on
        // success is not a record of what was seen.
        Assert.Equal(BuildTestData.CommitB, verdict.ObservedRemoteTargetCommitSha);
        Assert.NotNull(verdict.ObservedRemoteTagObjectSha);
        Assert.Contains(BuildTestData.CommitA, verdict.ExpectedTargetCommitShas);
    }

    /// <summary>
    /// C-1, second form — <b>a lightweight tag occupying the reference name is refused as drift.</b>
    ///
    /// <para>
    /// The cheapest thing someone with push access can do to a release reference, and the one that needs no
    /// cooperation from the governed publisher: <c>git push origin &lt;sha&gt;:refs/tags/release/unit/1.0.0</c>
    /// puts a tag at the right name that resolves straight to a commit and carries no message at all. There is
    /// no governed block to parse and no tag object to compare, so this is the case where the <i>name</i> is
    /// the only thing that matches — and the case rule 8 exists for.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NegativeControl1b_ALightweightRemoteTag_IsRefusedAsReferenceDrift()
    {
        using var fixture = await Fixture.CreateAsync();

        // Even pointing at the CERTIFIED commit, a lightweight tag is not the publisher's reference.
        fixture.Git.PublishLightweightTagToRemote(fixture.RefName, BuildTestData.CommitA);

        var verdict = await fixture.VerifyAsync();

        Assert.False(verdict.IsDeployable);
        Assert.Equal(ReleaseRefPreDeploymentState.ReferenceDrift, verdict.State);
        Assert.Equal(ReleaseRefPreDeploymentVerdict.ReleaseReferenceDriftVerdict, verdict.VerdictText);

        // The observation records the commit the lightweight tag resolves to, and no tag object — which is
        // what makes "there was nothing to compare identities with" visible in the evidence.
        Assert.Equal(BuildTestData.CommitA, verdict.ObservedRemoteTargetCommitSha);
        Assert.Null(verdict.ObservedRemoteTagObjectSha);
    }

    /// <summary>
    /// C-2 — <b>a moved tag is detected before deployment.</b>
    ///
    /// <para>
    /// The harder half of drift, and the reason the object id is checked and not only the target. The remote
    /// reference has been <i>re-created</i>: its name is unchanged, its target has been restored to the
    /// certified commit, and its annotation is byte-identical to the governed block. Every check that reads
    /// the tag's <i>content</i> passes. What cannot be restored is the tag object's identity, because a tag
    /// object's id covers its target, its tagger and its message — so a re-created reference is a different
    /// object even when it describes the same thing.
    /// </para>
    ///
    /// <para>
    /// This is the control that would catch a re-pointed reference whose author was careful. It is also the
    /// control that would silently become useless if the object identity went unchecked, which is why it is
    /// asserted separately from C-1 rather than folded into it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NegativeControl2_MovedRemoteTag_IsDetectedBeforeDeployment()
    {
        using var fixture = await Fixture.CreateAsync();

        // Same ref name, same target commit, same governed block text — and a different tagger, which is
        // what "re-created" means to git: the object id changes even though the description does not.
        fixture.Git.PublishAnnotatedTagToRemote(
            fixture.RefName,
            BuildTestData.CommitA,
            ReleaseTagAnnotation.Format(fixture.Release),
            tagger: "Someone Else <someone@else.invalid> 1789999999 +0000");

        var verdict = await fixture.VerifyAsync();

        Assert.False(verdict.IsDeployable);
        Assert.Equal(ReleaseRefPreDeploymentState.ReferenceDrift, verdict.State);
        Assert.Equal(ReleaseRefPreDeploymentVerdict.ReleaseReferenceDriftVerdict, verdict.VerdictText);

        // The target agrees — the drift was caught on the object identity alone. If this assertion ever
        // fails, the control has stopped testing what it says it tests.
        Assert.Equal(BuildTestData.CommitA, verdict.ObservedRemoteTargetCommitSha);
        Assert.NotEqual(verdict.ExpectedTagObjectSha, verdict.ObservedRemoteTagObjectSha);

        Assert.Contains(
            "not the one the governed publisher wrote",
            string.Join(" ", verdict.Detail),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// C-3 — <b>a fabricated tag cannot create a ReleaseId.</b>
    ///
    /// <para>
    /// Three fabrications, each refused in the place that owns the judgement rather than by a special case:
    /// a tag with no governed block is not a release reference (the assessor refuses it and the gate stops
    /// before the remote is consulted); a tag naming a different release cannot stand in for this one; and a
    /// reference cannot be created for a release that is not already in the immutable registry.
    /// </para>
    ///
    /// <para>
    /// The claim being tested is that a tag is <i>published for</i> a release and never <i>creates</i> one.
    /// The registry is asserted to be untouched in every case, and <c>TagCreationCount</c> is asserted to be
    /// zero throughout — the mechanism did not write a reference as a side effect of being asked about one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NegativeControl3_A_FabricatedTag_CannotCreateAReleaseId()
    {
        using var fixture = await Fixture.CreateAsync();

        // ---- (a) A tag with no governed block is a naming convention, not a reference -------------------
        var fabricated = fixture.Git;
        fabricated.WriteAnnotatedTag(fixture.RefName, BuildTestData.CommitA, "not a governed release block");

        var assessment = await new GitReleaseTagPolicy(fabricated)
            .AssessAsync(RepositoryLabel, ".", fixture.Release);

        Assert.False(assessment.IsGoverned);
        Assert.True(assessment.RefusedBecause(ReleaseTagRefusalReason.TagAnnotationNotGoverned));

        var gateVerdict = await fixture.VerifyAsync();

        Assert.False(gateVerdict.IsDeployable);
        Assert.Equal(ReleaseRefPreDeploymentState.ReferenceNotGoverned, gateVerdict.State);
        Assert.True(gateVerdict.RefusedBecause(ReleaseRefusalReason.ReleaseRefNotGoverned));

        // The remote was never consulted: with no governed local reference there is nothing to compare, and
        // querying it would suggest the comparison was possible.
        Assert.Equal(0, fabricated.RemoteQueryCount);

        // ---- (b) A tag naming a DIFFERENT release cannot stand in for this one -------------------------
        var other = ReleaseTestData.Record(identity: ReleaseTestData.Identity(digestFill: 'b'));

        Assert.NotEqual(fixture.Release.ReleaseId, other.ReleaseId);

        var namingAnotherRelease = new FakeGit();
        namingAnotherRelease.WriteAnnotatedTag(
            fixture.RefName,
            BuildTestData.CommitA,
            ReleaseTagAnnotation.Format(other));

        var borrowed = await new GitReleaseTagPolicy(namingAnotherRelease)
            .AssessAsync(RepositoryLabel, ".", fixture.Release);

        Assert.False(borrowed.IsGoverned);
        Assert.True(borrowed.RefusedBecause(ReleaseTagRefusalReason.TagIdentifiesDifferentRelease));

        // ---- (c) No reference may be created for a release that is not registered ----------------------
        var registry = new FakeReleaseRegistry();

        var unregistered = await new GitReleaseTagPublisher(
                namingAnotherRelease,
                new GitReleaseTagPolicy(namingAnotherRelease),
                registry)
            .PublishAsync(RepositoryLabel, ".", other, BuildTestData.CommitA);

        Assert.False(unregistered.IsCreated);
        Assert.True(unregistered.Created(ReleaseTagRefusalReason.ReleaseNotEligibleForReference));
        Assert.Equal(0, namingAnotherRelease.TagCreationCount);

        // The fabricated tag existed the whole time and a ReleaseId still did not come into existence.
        Assert.False(await registry.ExistsAsync(other.ReleaseId));

        // And creating a reference never reads a remote: the publisher's question is about the local chain,
        // and a publish step that needed the network to decide would be a publish step that failed offline.
        Assert.Equal(0, namingAnotherRelease.RemoteQueryCount);
    }

    /// <summary>
    /// C-4 — <b>a valid tag cannot substitute for a wrong artifact hash.</b>
    ///
    /// <para>
    /// The most tempting substitution in the whole control, and the one rule 8 names explicitly. The local
    /// reference is fully governed; the remote holds the byte-identical governed tag object at the certified
    /// source; the governed block even quotes an artifact digest. Everything about the reference agrees. The
    /// stored bytes are not the bytes the release records, and the deployment must stop anyway.
    /// </para>
    ///
    /// <para>
    /// The gate refuses without querying the remote at all, which is the structural part of the claim: the
    /// artifact check is not a check that runs <i>after</i> the tag agrees and can be skipped when it does,
    /// it is a check that runs before anything about the tag is read. Had the gate read rule 6's digest from
    /// the tag instead of from the release record, this control would be green and the deployment would move
    /// uncertified bytes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NegativeControl4_A_ValidTag_CannotSubstituteForAWrongArtifactHash()
    {
        // The record names an artifact digest the store cannot possibly hold, while the tag that quotes it is
        // perfect. The tag is arranged to be valid FIRST, so the refusal can only come from rule 6.
        using var fixture = await Fixture.CreateAsync(
            recordDigest: ArtifactDigest.Parse($"sha256:{new string('a', 64)}"));

        fixture.Git.PublishAnnotatedTagToRemote(
            fixture.RefName,
            BuildTestData.CommitA,
            ReleaseTagAnnotation.Format(fixture.Release));

        // Guard: the reference really is fully governed, so this test cannot pass for the wrong reason.
        var assessment = await new GitReleaseTagPolicy(fixture.Git)
            .AssessAsync(RepositoryLabel, ".", fixture.Release);

        Assert.True(assessment.IsGoverned);

        var verdict = await fixture.VerifyAsync();

        Assert.False(verdict.IsDeployable);
        Assert.Equal(ReleaseRefPreDeploymentState.ArtifactBytesUnverified, verdict.State);
        Assert.True(verdict.RefusedBecause(ReleaseRefusalReason.ArtifactHashMismatch));

        // The remote was never read: the bytes were judged against the release record before anything about
        // the reference was consulted.
        Assert.Equal(0, fixture.Git.RemoteQueryCount);

        // And rule 9's observation is honestly reported as not made, rather than as agreeing.
        Assert.Null(verdict.ObservedRemoteTagObjectSha);
    }

    /// <summary>
    /// C-5 — <b>a correct Release Bundle remains deployable when all identities agree.</b>
    ///
    /// <para>
    /// The positive control, without which the four above are satisfied by a gate that refuses everything. A
    /// governed local reference, its identical object on the remote, the certified source commit, and stored
    /// bytes that hash to the digest the release records: the gate verifies, and rule 9's observed remote
    /// identity is recorded.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NegativeControl5_A_CorrectReleaseBundle_RemainsDeployable()
    {
        using var fixture = await Fixture.CreateAsync();

        fixture.Git.PublishAnnotatedTagToRemote(
            fixture.RefName,
            BuildTestData.CommitA,
            ReleaseTagAnnotation.Format(fixture.Release));

        var verdict = await fixture.VerifyAsync();

        Assert.True(verdict.IsDeployable);
        Assert.Equal(ReleaseRefPreDeploymentState.Verified, verdict.State);
        Assert.Equal(ReleaseRefPreDeploymentVerdict.VerifiedVerdict, verdict.VerdictText);
        Assert.Empty(verdict.RefusalReasons);
        Assert.False(verdict.IsReferenceDrift);

        // The remote was actually read, exactly once, and the identity it reported is the governed object.
        Assert.Equal(1, fixture.Git.RemoteQueryCount);
        Assert.Equal(verdict.ExpectedTagObjectSha, verdict.ObservedRemoteTagObjectSha);
        Assert.Equal(BuildTestData.CommitA, verdict.ObservedRemoteTargetCommitSha);

        // The verification must not claim a server-side protection that this estate does not have. The
        // substitute is recorded as the substitute.
        Assert.Contains(
            "NOT claimed",
            string.Join(" ", verdict.Detail),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Remote absence is its own state, and it is neither drift nor a pass.</b>
    ///
    /// <para>
    /// This is the state the live remote is in today, and the reason the deviation had to name it. Nothing
    /// has been published, so nothing has been verified. Reporting it as drift would cry attack on an
    /// ordinary stage boundary; reporting it as verified would report a check that never ran. It is refused,
    /// and the refusal is distinguishable from both.
    /// </para>
    /// </summary>
    [Fact]
    public async Task RemoteAbsence_IsRefusedAsItsOwnState_NotAsDriftAndNotAsVerified()
    {
        using var fixture = await Fixture.CreateAsync();

        var verdict = await fixture.VerifyAsync();

        Assert.False(verdict.IsDeployable);
        Assert.Equal(ReleaseRefPreDeploymentState.RemoteReferenceAbsent, verdict.State);
        Assert.Equal(ReleaseRefPreDeploymentVerdict.RemoteReferenceAbsentVerdict, verdict.VerdictText);

        // The distinction that matters: absence is NOT drift, so a reader watching for RELEASE_REFERENCE_DRIFT
        // does not see this.
        Assert.False(verdict.IsReferenceDrift);
        Assert.True(verdict.RefusedBecause(ReleaseRefusalReason.RemoteReleaseRefAbsent));
        Assert.DoesNotContain(ReleaseRefusalReason.ReleaseReferenceDrift, verdict.RefusalReasons);

        // The remote was queried and it answered — absence is a positive finding, not a failure to read.
        Assert.Equal(1, fixture.Git.RemoteQueryCount);

        // And the local reference genuinely was governed, so absence is not being confused with a bad release.
        Assert.NotNull(verdict.ExpectedTagObjectSha);
    }

    /// <summary>
    /// <b>A remote that could not be read is refused, and is distinguishable from one that answered.</b>
    /// Absence of evidence is a refusal: "could not check" is never "checked".
    /// </summary>
    [Fact]
    public async Task AnUnreachableRemote_IsRefusedAsUnread_NotAsAbsent()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Git.RemoteIsUnreachable = true;

        var verdict = await fixture.VerifyAsync();

        Assert.False(verdict.IsDeployable);
        Assert.Equal(ReleaseRefPreDeploymentState.RemoteUnreachable, verdict.State);
        Assert.True(verdict.RefusedBecause(ReleaseRefusalReason.RemoteReleaseRefUnreachable));
        Assert.NotEqual(ReleaseRefPreDeploymentState.RemoteReferenceAbsent, verdict.State);

        // The diagnostic is carried, because an operator has to be able to tell a dead network from a
        // missing tag.
        Assert.Contains("Could not resolve host", string.Join(" ", verdict.Detail), StringComparison.Ordinal);
    }

    /// <summary>
    /// The gate reports the protection status it observed instead of restating it as protected, and it names
    /// the deviation. A substituted control that reported itself as the original would be the overstatement
    /// the deviation exists to prevent.
    /// </summary>
    [Fact]
    public async Task TheVerdict_RecordsTheObservedProtectionStatus_AndDoesNotClaimARuleset()
    {
        using var fixture = await Fixture.CreateAsync();

        fixture.Git.PublishAnnotatedTagToRemote(
            fixture.RefName,
            BuildTestData.CommitA,
            ReleaseTagAnnotation.Format(fixture.Release));

        var verdict = await fixture.VerifyAsync();
        var detail = string.Join(" ", verdict.Detail);

        Assert.True(verdict.IsDeployable);
        Assert.Contains(ReleaseRefProtectionStatus.RequiresServerSideVerification.ToString(), detail, StringComparison.Ordinal);
        Assert.Contains("C-2", detail, StringComparison.Ordinal);
        Assert.Contains("ENV-DEV", detail, StringComparison.Ordinal);

        // Nothing anywhere claims a ruleset was observed.
        Assert.DoesNotContain("ruleset protection verified", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Protected", detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A disposable fixture: real store bytes under a temp root, real policies and gate, in-memory git with
    /// an in-memory remote, and a governed local tag for a release whose artifact is genuinely in the store.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly TempRoot _root = new("w94-c2");

        private Fixture(FakeGit git) => Git = git;

        public FakeGit Git { get; }

        public ReleaseRecord Release { get; private set; } = null!;

        public ReleaseRefPreDeploymentGate Gate { get; private set; } = null!;

        public string RefName => IReleaseTagPolicy.RefNameFor(Release);

        public static async Task<Fixture> CreateAsync(
            ArtifactDigest? recordDigest = null,
            bool governedTagPresent = true)
        {
            var git = new FakeGit();
            var fixture = new Fixture(git);

            // Real bytes, really hashed, in a store that is deleted with the fixture.
            var published = await ReleaseTestData.PublishedArtifactAsync(fixture._root.Path);

            fixture.Release = ReleaseTestData.Record(
                identity: ReleaseTestData.Identity(
                    artifactDigest: recordDigest ?? published.Digest,
                    artifactSize: published.Artifact.SizeBytes));

            if (governedTagPresent)
            {
                git.WriteAnnotatedTag(
                    fixture.RefName,
                    BuildTestData.CommitA,
                    ReleaseTagAnnotation.Format(fixture.Release));
            }

            fixture.Gate = new ReleaseRefPreDeploymentGate(
                new GitReleaseTagPolicy(git),
                new GitRemoteReleaseTagVerifier(git),
                published.Store);

            return fixture;
        }

        public Task<ReleaseRefPreDeploymentVerdict> VerifyAsync() =>
            Gate.VerifyAsync(RepositoryLabel, ".", Remote, Release);

        public void Dispose() => _root.Dispose();
    }
}
