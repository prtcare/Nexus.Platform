using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// Assembly of a Release Bundle from a certified artifact, and the no-rebuild rule.
///
/// <para>
/// The negative controls are the substance here. "W9.3 consumes the W9.2 artifact and does not rebuild"
/// is only a control if presenting different bytes is refused by something; otherwise it is a sentence.
/// </para>
/// </summary>
public sealed class ReleaseAssemblyTests : IDisposable
{
    private readonly TempRoot _root = new("assembly");

    public void Dispose() => _root.Dispose();

    private static ReleaseAssemblyRequest RequestFor(BuildManifest manifest, char digestFill = 'a')
        => new(
            manifest,
            BuildManifestCodec.ComputeManifestDigest(manifest),
            ReleaseTestData.BundleId,
            "W9_2_BUILD_ARTIFACT/BUILD_MANIFEST.txt",
            [new ContractVersion("Nexus.ProductCore.Contracts", "0.1.0")],
            ReleaseTestData.NoMigrations,
            ReleaseTestData.FirstRelease,
            ReleaseTestData.Health,
            "runtime-config-v1",
            DateTimeOffset.Parse("2026-09-23T10:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
            configurationKeys: ["api"],
            originatingWork: new GovernedWorkReference(ReleaseTestData.WorkReference));

    [Fact]
    public async Task Assemble_AcceptsACertifiedArtifact_AndTakesTheVersionFromIt()
    {
        var fixture = await ReleaseTestData.PublishedArtifactAsync(_root.Path);

        Assert.True(fixture.Outcome.IsAccepted);

        var assembler = new ReleaseBundleAssembler(fixture.Store);
        var outcome = await assembler.AssembleAsync(RequestFor(fixture.Manifest));

        Assert.True(outcome.IsAssembled);

        var release = outcome.Release!;

        Assert.Equal("0.1.0", release.Version);
        Assert.Equal(fixture.Artifact.ArtifactId, release.Artifacts[0].ArtifactId);
        Assert.Equal(fixture.Digest, release.Artifacts[0].ContentDigest);
        Assert.Equal(fixture.Artifact.BuildId, release.BuildId);
        Assert.Equal(fixture.Manifest.Identity.Sources[0].CommitSha, release.Identity.SourceCommits[0]);
        Assert.Equal("refs/tags/release/marketsurvey.api/0.1.0", release.Identity.ReleaseRefName);
    }

    /// <summary>
    /// The release records the digest it was <b>given</b>, not one it derived from the manifest in hand.
    ///
    /// <para>
    /// Those coincide when the manifest passed in is the certified one, and diverge when it is a
    /// reconstruction — which is exactly the case the first real release hit. The release then named W9.2's
    /// manifest as its evidence while recording a digest of something else, so anyone following the
    /// reference would compute a different value and the chain would not close. This asserts the supplied
    /// value wins, by supplying one that deliberately disagrees with the manifest beside it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Assemble_RecordsTheSuppliedManifestDigest_NotOneDerivedFromTheManifestInHand()
    {
        var fixture = await ReleaseTestData.PublishedArtifactAsync(_root.Path);
        var certified = ArtifactDigest.Parse($"sha256:{new string('c', 64)}");

        var request = new ReleaseAssemblyRequest(
            fixture.Manifest,
            certified,
            ReleaseTestData.BundleId,
            "W9_2_BUILD_ARTIFACT/BUILD_MANIFEST.txt",
            [new ContractVersion("Nexus.ProductCore.Contracts", "0.1.0")],
            ReleaseTestData.NoMigrations,
            ReleaseTestData.FirstRelease,
            ReleaseTestData.Health,
            "runtime-config-v1",
            DateTimeOffset.UnixEpoch,
            configurationKeys: ["api"]);

        var outcome = await new ReleaseBundleAssembler(fixture.Store).AssembleAsync(request);

        Assert.True(outcome.IsAssembled);
        Assert.Equal(certified, outcome.Release!.Evidence.BuildManifestDigest);
        Assert.NotEqual(BuildManifestCodec.ComputeManifestDigest(fixture.Manifest), outcome.Release.Evidence.BuildManifestDigest);
    }

    [Fact]
    public async Task Assemble_RefusesWhenTheCertifiedArtifactIsNotInTheStore()
    {
        // A store that holds nothing, and a manifest that names an artifact.
        var emptyRoot = System.IO.Path.Combine(_root.Path, "empty");
        var store = new FileArtifactStore(emptyRoot);
        var manifest = BuildTestData.Manifest();

        var outcome = await new ReleaseBundleAssembler(store).AssembleAsync(RequestFor(manifest));

        Assert.False(outcome.IsAssembled);
        Assert.True(outcome.RefusedBecause(ReleaseRefusalReason.ArtifactNotRegistered));
        Assert.Contains("never from a claim that they exist", string.Join(" ", outcome.Detail), StringComparison.Ordinal);
    }

    /// <summary>
    /// TASK 12's negative control: modified bytes presented after certification must be refused.
    ///
    /// <para>
    /// The artifact is already published and immutable, so a rebuild cannot be smuggled in under the same
    /// id — the store refuses it at publish time. What this asserts is the other half: given a store whose
    /// bytes have been replaced underneath it, assembly re-hashes the content and refuses rather than
    /// assembling a release that names bytes it does not have.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Assemble_RefusesWhenTheStoredBytesWereModifiedAfterCertification()
    {
        var fixture = await ReleaseTestData.PublishedArtifactAsync(_root.Path);

        var contentPath = System.IO.Path.Combine(
            _root.Path,
            "artifacts",
            fixture.Artifact.ArtifactId.Value.Replace('/', System.IO.Path.DirectorySeparatorChar),
            "content.bin");

        Assert.True(File.Exists(contentPath));

        // Same length, different bytes: the size check cannot catch this, only the re-hash can.
        var original = await File.ReadAllBytesAsync(contentPath);
        var tampered = (byte[])original.Clone();
        tampered[0] = (byte)(tampered[0] ^ 0xFF);
        await File.WriteAllBytesAsync(contentPath, tampered);

        var outcome = await new ReleaseBundleAssembler(fixture.Store).AssembleAsync(RequestFor(fixture.Manifest));

        Assert.False(outcome.IsAssembled);
        Assert.True(outcome.RefusedBecause(ReleaseRefusalReason.ArtifactHashMismatch));
        Assert.Equal(original.Length, tampered.Length);
    }

    /// <summary>
    /// And the store's own rule, which is the first line: the same artifact id can never hold different
    /// bytes, so a "rebuilt" artifact cannot replace a certified one even before anything is assembled.
    /// </summary>
    [Fact]
    public async Task Store_RefusesToRepublishAnArtifactIdWithDifferentBytes()
    {
        var fixture = await ReleaseTestData.PublishedArtifactAsync(_root.Path);

        var replacement = System.IO.Path.Combine(_root.Path, "rebuilt.bin");
        var differentContent = System.Text.Encoding.UTF8.GetBytes("nexus-w9.3-rebuilt-" + TestData.RandomAlnum(24));
        await File.WriteAllBytesAsync(replacement, differentContent);

        // The manifest is the SAME one, so the id and the claimed digest are the certified ones while the
        // bytes are not: this is exactly "modified bytes presented after certification".
        var outcome = await fixture.Store.PublishAsync(fixture.Artifact, replacement);

        Assert.False(outcome.IsAccepted);
        Assert.Equal(ArtifactPublishRefusalReason.ContentHashMismatch, outcome.RefusalReason);

        // And when the digest is corrected to match the new bytes, the ID rule fires instead: changed bytes
        // are a NEW build, never a re-publish under an existing id.
        var rebuilt = new PackagedArtifact(
            fixture.Artifact.ArtifactId,
            ArtifactDigest.Compute(differentContent),
            differentContent.Length,
            fixture.Artifact.BuildId,
            "marketsurvey.api.zip");

        var second = await fixture.Store.PublishAsync(rebuilt, replacement);

        Assert.False(second.IsAccepted);
        Assert.Equal(ArtifactPublishRefusalReason.ArtifactIdExistsWithDifferentBytes, second.RefusalReason);
        Assert.Contains("NEW build", second.Detail!, StringComparison.Ordinal);
    }

    /// <summary>Re-publishing byte-identical content is a no-op, so a retried release run succeeds rather than failing for having succeeded.</summary>
    [Fact]
    public async Task Store_AcceptsARepublishOfIdenticalBytesAsANoOp()
    {
        var fixture = await ReleaseTestData.PublishedArtifactAsync(_root.Path);

        var copy = System.IO.Path.Combine(_root.Path, "copy.bin");
        var contentPath = System.IO.Path.Combine(
            _root.Path,
            "artifacts",
            fixture.Artifact.ArtifactId.Value.Replace('/', System.IO.Path.DirectorySeparatorChar),
            "content.bin");

        File.Copy(contentPath, copy);

        var outcome = await fixture.Store.PublishAsync(fixture.Artifact, copy);

        Assert.True(outcome.IsAccepted);
        Assert.True(outcome.IsAlreadyPresent);
    }
}

/// <summary>The release lineage chain and its reverse navigation.</summary>
public sealed class ReleaseLineageTests : IDisposable
{
    private readonly TempRoot _root = new("lineage");

    public void Dispose() => _root.Dispose();

    private string StoreRoot => System.IO.Path.Combine(_root.Path, "store");

    private string LineageRoot => System.IO.Path.Combine(_root.Path, "lineage");

    [Fact]
    public async Task WalkBackFromRelease_ResolvesEveryLink_FromTheGovernedWorkToTheRelease()
    {
        var fixture = await ReleaseTestData.PublishedArtifactAsync(StoreRoot);
        var release = await AssembleAsync(fixture);

        var registry = new FileReleaseRegistry(StoreRoot);
        var log = new FileReleaseLineageLog(LineageRoot);

        await registry.RegisterAsync(release);
        await log.AppendAsync(new ReleaseLineageRecord(
            "L-W9-1",
            DateTimeOffset.UnixEpoch,
            release.ReleaseId,
            release.UnitId,
            release.Version,
            release.BuildId,
            release.BundleId,
            release.Identity.ReleaseRefName,
            release.Identity.SourceCommits,
            release.Artifacts,
            new GovernedWorkReference(ReleaseTestData.WorkReference)));

        var walk = await new ReleaseLineageResolver(registry, fixture.Store, log)
            .WalkBackFromReleaseAsync(release.ReleaseId, fixture.Manifest);

        Assert.True(walk.IsComplete, walk.Detail);
        Assert.Null(walk.StoppedAt);

        // Outer to inner: the governed work, then the commit, the build, the artifact, the release.
        Assert.Equal(
            [
                ReleaseLineageHopKind.GovernedWork,
                ReleaseLineageHopKind.SourceCommit,
                ReleaseLineageHopKind.Build,
                ReleaseLineageHopKind.Artifact,
                ReleaseLineageHopKind.Release
            ],
            walk.Hops.Select(h => h.Kind));

        Assert.Equal(ReleaseTestData.WorkReference, walk.Hops[0].Identity);
        Assert.Equal(fixture.Manifest.Identity.Sources[0].CommitSha, walk.Hops[1].Identity);
        Assert.Equal(release.BuildId.Value, walk.Hops[2].Identity);
        Assert.Equal(fixture.Artifact.ArtifactId.Value, walk.Hops[3].Identity);
        Assert.Equal(release.ReleaseId.Value, walk.Hops[4].Identity);

        Assert.Contains("→", walk.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WalkBackFromRelease_StopsAndNamesTheLinkThatDoesNotResolve()
    {
        // A release registered in a registry whose artifact store holds nothing: the artifact link is the
        // one that cannot resolve, and the walk must say so rather than recite the record.
        var registry = new FileReleaseRegistry(StoreRoot);
        var release = ReleaseTestData.Record();
        await registry.RegisterAsync(release);

        var emptyStore = new FileArtifactStore(System.IO.Path.Combine(_root.Path, "empty"));
        var walk = await new ReleaseLineageResolver(registry, emptyStore, new FileReleaseLineageLog(LineageRoot))
            .WalkBackFromReleaseAsync(release.ReleaseId);

        Assert.False(walk.IsComplete);
        Assert.Equal(ReleaseLineageHopKind.Artifact, walk.StoppedAt);
        Assert.Contains("not in the artifact store", walk.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The outermost link is the one nothing in a build records, so a release whose artifact, build and
    /// commits all resolve still stops there — and says which link it stopped at rather than filling it in.
    /// </summary>
    [Fact]
    public async Task WalkBackFromRelease_NamesTheOutermostLinkAsUnresolvedWhenNoLineageRecordExists()
    {
        var fixture = await ReleaseTestData.PublishedArtifactAsync(StoreRoot);
        var release = await AssembleAsync(fixture);

        var registry = new FileReleaseRegistry(StoreRoot);
        await registry.RegisterAsync(release);

        var walk = await new ReleaseLineageResolver(registry, fixture.Store, new FileReleaseLineageLog(LineageRoot))
            .WalkBackFromReleaseAsync(release.ReleaseId, fixture.Manifest);

        Assert.False(walk.IsComplete);
        Assert.Equal(ReleaseLineageHopKind.GovernedWork, walk.StoppedAt);

        // Everything before it resolved, which is what makes the stopping point informative.
        Assert.Equal(4, walk.Hops.Count);
        Assert.Contains("governed work it was performed for", walk.Detail, StringComparison.Ordinal);
    }

    private static async Task<ReleaseRecord> AssembleAsync(ReleaseTestData.PublishedFixture fixture)
    {
        var outcome = await new ReleaseBundleAssembler(fixture.Store).AssembleAsync(new ReleaseAssemblyRequest(
            fixture.Manifest,
            BuildManifestCodec.ComputeManifestDigest(fixture.Manifest),
            ReleaseTestData.BundleId,
            "W9_2_BUILD_ARTIFACT/BUILD_MANIFEST.txt",
            [new ContractVersion("Nexus.ProductCore.Contracts", "0.1.0")],
            ReleaseTestData.NoMigrations,
            ReleaseTestData.FirstRelease,
            ReleaseTestData.Health,
            "runtime-config-v1",
            DateTimeOffset.UnixEpoch,
            configurationKeys: ["api"],
            originatingWork: new GovernedWorkReference(ReleaseTestData.WorkReference)));

        return outcome.Release!;
    }

    [Fact]
    public async Task LineageLog_RefusesADuplicateLineageId()
    {
        var log = new FileReleaseLineageLog(LineageRoot);
        var release = ReleaseTestData.Record();

        await log.AppendAsync(Record("L-W9-1", release));

        await Assert.ThrowsAsync<InvalidOperationException>(() => log.AppendAsync(Record("L-W9-1", release)));
    }

    /// <summary>
    /// A release occupies one position in the chain. A second row claiming the same position is a correction
    /// attempt wearing an append's clothing, and the estate's rule is to re-execute rather than patch.
    /// </summary>
    [Fact]
    public async Task LineageLog_RefusesASecondRecordForTheSameRelease()
    {
        var log = new FileReleaseLineageLog(LineageRoot);
        var release = ReleaseTestData.Record();

        await log.AppendAsync(Record("L-W9-1", release));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => log.AppendAsync(Record("L-W9-2", release)));

        Assert.Contains("supersede it with a new release", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LineageLog_ReadsBackInNumericOrder_SoL10FollowsL9()
    {
        var log = new FileReleaseLineageLog(LineageRoot);

        await log.AppendAsync(Record("L-W9-10", ReleaseTestData.Record(identity: ReleaseTestData.Identity(digestFill: 'b'))));
        await log.AppendAsync(Record("L-W9-9", ReleaseTestData.Record(identity: ReleaseTestData.Identity(digestFill: 'a'))));

        var records = await log.ReadAsync();

        Assert.Equal(["L-W9-9", "L-W9-10"], records.Select(r => r.LineageId));
    }

    [Fact]
    public void LineageRecord_RefusesAnIdOutsideTheEstateSeries()
    {
        var release = ReleaseTestData.Record();

        Assert.Throws<ArgumentException>(() => Record("W93-1", release));
        Assert.Throws<ArgumentException>(() => Record("L-W9-", release));
    }

    private static ReleaseLineageRecord Record(string lineageId, ReleaseRecord release)
        => new(
            lineageId,
            DateTimeOffset.UnixEpoch,
            release.ReleaseId,
            release.UnitId,
            release.Version,
            release.BuildId,
            release.BundleId,
            release.Identity.ReleaseRefName,
            release.Identity.SourceCommits,
            release.Artifacts,
            new GovernedWorkReference(ReleaseTestData.WorkReference));
}
