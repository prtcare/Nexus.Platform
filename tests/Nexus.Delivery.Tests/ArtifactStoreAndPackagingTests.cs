using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// The artifact store: immutability, verification, lifecycle and resolution. Immutability is the property
/// the whole promotion guarantee rests on, so it gets the negative controls.
/// </summary>
public sealed class ArtifactStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _source;

    public ArtifactStoreTests()
    {
        _root = NewTemp("store");
        _source = NewTemp("src");
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _root, _source })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static string NewTemp(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexus-w92-{prefix}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private FileArtifactStore Store => new(_root);

    /// <summary>Writes a file and returns the artifact describing it, hashing what was actually written.</summary>
    private PackagedArtifact Stage(BuildIdentity identity, string content, char? declaredFill = null, string version = "0.1.0")
    {
        var path = Path.Combine(_source, $"artifact-{version}.zip");
        File.WriteAllText(path, content);

        ArtifactDigest digest;
        using (var stream = File.OpenRead(path))
        {
            digest = ArtifactDigest.Compute(stream);
        }

        if (declaredFill is not null)
        {
            digest = ArtifactDigest.Parse($"sha256:{new string(declaredFill.Value, 64)}");
        }

        return new PackagedArtifact(
            ArtifactId.For(identity.UnitId, ArtifactType.DotnetApplication, "marketsurvey.api", version),
            digest,
            new FileInfo(path).Length,
            identity.BuildId,
            "marketsurvey.api.zip");
    }

    [Fact]
    public async Task Publish_AcceptsAFreshArtifact_AndHashesTheBytesItself()
    {
        var identity = BuildTestData.Identity();
        var artifact = Stage(identity, "round one content");
        var store = Store;

        var outcome = await store.PublishAsync(artifact, Path.Combine(_source, "artifact-0.1.0.zip"));

        Assert.True(outcome.IsAccepted);
        Assert.False(outcome.IsAlreadyPresent);
        Assert.NotNull(outcome.Entry);
        Assert.Equal(artifact.ContentDigest, outcome.Entry!.ContentDigest);
        Assert.Equal(ArtifactLifecycleState.Published, outcome.Entry.Lifecycle);
        Assert.True(await store.ExistsAsync(artifact.ArtifactId));
    }

    /// <summary>
    /// Immutability, and the reason it is phrased this way rather than as W9.1's flat refusal: an idempotent
    /// re-publish of identical bytes changes nothing, so refusing it would fail a retried pipeline run while
    /// adding no protection. What must never be accepted is the same id with different bytes.
    /// </summary>
    [Fact]
    public async Task Publish_RefusesTheSameIdWithDifferentBytes()
    {
        var identity = BuildTestData.Identity();
        var store = Store;

        var first = Stage(identity, "the original content");
        Assert.True((await store.PublishAsync(first, Path.Combine(_source, "artifact-0.1.0.zip"))).IsAccepted);

        // Same coordinate, different content. This is the rule: changed bytes are a NEW build.
        var second = Stage(identity, "replacement content, which must be refused");
        var outcome = await store.PublishAsync(second, Path.Combine(_source, "artifact-0.1.0.zip"));

        Assert.False(outcome.IsAccepted);
        Assert.Equal(ArtifactPublishRefusalReason.ArtifactIdExistsWithDifferentBytes, outcome.RefusalReason);
        Assert.Contains("NEW build", outcome.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_AcceptsIdenticalBytesUnderTheSameId_AsANoOp()
    {
        var identity = BuildTestData.Identity();
        var store = Store;

        var artifact = Stage(identity, "byte identical content");
        Assert.True((await store.PublishAsync(artifact, Path.Combine(_source, "artifact-0.1.0.zip"))).IsAccepted);

        var again = await store.PublishAsync(artifact, Path.Combine(_source, "artifact-0.1.0.zip"));

        Assert.True(again.IsAccepted);
        Assert.True(again.IsAlreadyPresent);
    }

    [Fact]
    public async Task Publish_RefusesBytesThatDoNotMatchTheClaimedDigest()
    {
        var identity = BuildTestData.Identity();
        var artifact = Stage(identity, "content", declaredFill: 'f');
        var store = Store;

        var outcome = await store.PublishAsync(artifact, Path.Combine(_source, "artifact-0.1.0.zip"));

        Assert.Equal(ArtifactPublishRefusalReason.ContentHashMismatch, outcome.RefusalReason);
    }

    [Fact]
    public async Task Publish_RefusesAMissingSourceFile()
    {
        var identity = BuildTestData.Identity();
        var store = Store;

        var outcome = await store.PublishAsync(Stage(identity, "x"), Path.Combine(_source, "does-not-exist.zip"));

        Assert.Equal(ArtifactPublishRefusalReason.SourceFileMissing, outcome.RefusalReason);
    }

    [Fact]
    public async Task Fetch_ReturnsTheBytesAndVerifiesThem()
    {
        var identity = BuildTestData.Identity();
        var store = Store;
        var artifact = Stage(identity, "fetchable content");

        await store.PublishAsync(artifact, Path.Combine(_source, "artifact-0.1.0.zip"));

        var destination = Path.Combine(_source, "fetched.zip");
        var outcome = await store.FetchAsync(artifact.ArtifactId, destination);

        Assert.Equal(ArtifactFetchStatus.Fetched, outcome.Status);
        Assert.Equal("fetchable content", File.ReadAllText(destination));
    }

    /// <summary>
    /// Corruption is a distinct outcome from absence. A boolean fetch would conflate them, and the two
    /// demand opposite responses: a missing artifact is a missing promotion input, while a corrupt one means
    /// every artifact from that store is suspect.
    /// </summary>
    [Fact]
    public async Task Fetch_ReportsCorruption_WhenTheStoredBytesChanged()
    {
        var identity = BuildTestData.Identity();
        var store = Store;
        var artifact = Stage(identity, "content that will be tampered with");

        await store.PublishAsync(artifact, Path.Combine(_source, "artifact-0.1.0.zip"));

        // Tamper with the stored bytes, behind the store's back.
        var stored = Path.Combine(_root, "artifacts", "marketsurvey.api", "dotnet-app", "marketsurvey.api@0.1.0", "content.bin");
        File.WriteAllText(stored, "tampered content");

        var outcome = await store.FetchAsync(artifact.ArtifactId, Path.Combine(_source, "fetched.zip"));

        Assert.Equal(ArtifactFetchStatus.Corrupt, outcome.Status);
    }

    [Fact]
    public async Task Fetch_OfAnAbsentArtifact_IsAbsent()
    {
        var identity = BuildTestData.Identity();
        var artifact = Stage(identity, "x");

        var outcome = await Store.FetchAsync(artifact.ArtifactId, Path.Combine(_source, "nope.zip"));

        Assert.Equal(ArtifactFetchStatus.Absent, outcome.Status);
    }

    [Fact]
    public async Task VerifyHash_MatchesWhenTheStoreIsIntact()
    {
        var identity = BuildTestData.Identity();
        var store = Store;
        var artifact = Stage(identity, "verifiable content");

        await store.PublishAsync(artifact, Path.Combine(_source, "artifact-0.1.0.zip"));

        var verification = await store.VerifyHashAsync(artifact.ArtifactId, artifact.ContentDigest);

        Assert.True(verification.IsMatch);
        Assert.True(verification.StoreContentIntact);
    }

    [Fact]
    public async Task VerifyHash_FailsAgainstAWrongExpectation()
    {
        var identity = BuildTestData.Identity();
        var store = Store;
        var artifact = Stage(identity, "verifiable content");

        await store.PublishAsync(artifact, Path.Combine(_source, "artifact-0.1.0.zip"));

        var verification = await store.VerifyHashAsync(artifact.ArtifactId, ArtifactDigest.Parse($"sha256:{new string('9', 64)}"));

        Assert.False(verification.IsMatch);
    }

    [Fact]
    public async Task ResolveByArtifactId_ReturnsTheEntry()
    {
        var identity = BuildTestData.Identity();
        var store = Store;
        var artifact = Stage(identity, "resolvable content");

        await store.PublishAsync(artifact, Path.Combine(_source, "artifact-0.1.0.zip"));

        var entry = await store.ResolveByArtifactIdAsync(artifact.ArtifactId);

        Assert.NotNull(entry);
        Assert.Equal(identity.BuildId, entry!.BuildId);
        Assert.Null(await store.ResolveByArtifactIdAsync(ArtifactId.For(identity.UnitId, ArtifactType.DotnetApplication, "marketsurvey.api", "9.9.9")));
    }

    [Fact]
    public async Task ResolveByBuildId_ReturnsEveryArtifactOfOneBuild_InAStableOrder()
    {
        var identity = BuildTestData.Identity();
        var store = Store;

        var first = Stage(identity, "first", version: "0.1.0");
        var second = Stage(identity, "second", version: "0.1.1");

        await store.PublishAsync(first, Path.Combine(_source, "artifact-0.1.0.zip"));
        await store.PublishAsync(second, Path.Combine(_source, "artifact-0.1.1.zip"));

        var entries = await store.ResolveByBuildIdAsync(identity.BuildId);

        Assert.Equal(2, entries.Count);
        Assert.Equal(entries.OrderBy(e => e.ArtifactId.Value, StringComparer.Ordinal).Select(e => e.ArtifactId.Value), entries.Select(e => e.ArtifactId.Value));
    }

    [Fact]
    public async Task ResolveByBuildId_OfAnUnknownBuild_IsEmpty()
    {
        Assert.Empty(await Store.ResolveByBuildIdAsync(BuildTestData.Identity().BuildId));
    }

    [Fact]
    public async Task AQuarantinedArtifact_IsPresentButNotFetchable()
    {
        var identity = BuildTestData.Identity();
        var store = Store;
        var artifact = Stage(identity, "quarantined content");

        await store.PublishAsync(artifact, Path.Combine(_source, "artifact-0.1.0.zip"));
        await store.SetLifecycleAsync(artifact.ArtifactId, ArtifactLifecycleState.Quarantined, "A credential is reachable from this build's source.");

        Assert.True(await store.ExistsAsync(artifact.ArtifactId));

        var outcome = await store.FetchAsync(artifact.ArtifactId, Path.Combine(_source, "fetched.zip"));

        Assert.Equal(ArtifactFetchStatus.Quarantined, outcome.Status);
    }

    [Fact]
    public async Task ALifecycleChange_RequiresAReason()
    {
        var identity = BuildTestData.Identity();
        var store = Store;
        var artifact = Stage(identity, "x");
        await store.PublishAsync(artifact, Path.Combine(_source, "artifact-0.1.0.zip"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SetLifecycleAsync(artifact.ArtifactId, ArtifactLifecycleState.Retired, "   "));
    }

    [Fact]
    public async Task ALifecycleChange_OnAnUnpublishedArtifact_IsRefused()
    {
        var identity = BuildTestData.Identity();
        var artifact = Stage(identity, "x");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Store.SetLifecycleAsync(artifact.ArtifactId, ArtifactLifecycleState.Retired, "gone"));
    }

    [Fact]
    public void ArtifactId_IsTheImmutableCoordinate_SoAVersionCannotBeRepublished()
    {
        var id = ArtifactId.For(BuildTestData.Unit, ArtifactType.DotnetApplication, "marketsurvey.api", "0.1.0");

        Assert.Equal("marketsurvey.api/dotnet-app/marketsurvey.api@0.1.0", id.Value);
        Assert.Equal("0.1.0", id.Version);

        // A different version is a different coordinate, which is what lets a new build publish at all.
        Assert.NotEqual(id.Value, ArtifactId.For(BuildTestData.Unit, ArtifactType.DotnetApplication, "marketsurvey.api", "0.1.1").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1")]
    [InlineData("1.2.x")]
    [InlineData("1..2")]
    public void ArtifactId_RefusesAnUnusableVersion(string version)
    {
        Assert.Throws<ArgumentException>(() => ArtifactId.For(BuildTestData.Unit, ArtifactType.DotnetApplication, "marketsurvey.api", version));
    }
}

/// <summary>Packaging: determinism, and the negative obligation not to inject anything.</summary>
public sealed class ArtifactPackagingTests : IDisposable
{
    private readonly string _root;

    public ArtifactPackagingTests() => _root = Path.Combine(Path.GetTempPath(), "nexus-w92-pack-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string StageDirectory(string name, params (string Path, string Content)[] files)
    {
        var directory = Path.Combine(_root, name, "staging");
        Directory.CreateDirectory(directory);

        foreach (var (path, content) in files)
        {
            var full = Path.Combine(directory, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        return directory;
    }

    /// <summary>
    /// The property that makes a reproducibility proof possible at all. A zip records each entry's
    /// modification time, and the default is the file's timestamp on disk — so a naive archive of the same
    /// content differs run to run, and the failure would look like a build problem. The packager fixes every
    /// entry's time and sorts entries, so the archive is a pure function of its content.
    /// </summary>
    [Fact]
    public async Task ZipPackaging_IsByteIdenticalForIdenticalContent_AcrossDirectoriesAndTimes()
    {
        var identity = BuildTestData.Identity();
        var artifactId = ArtifactId.For(identity.UnitId, ArtifactType.DotnetApplication, "marketsurvey.api", "0.1.0");

        var first = StageDirectory("one", ("a.dll", "alpha"), ("sub/b.json", "{\"k\":1}"));
        var second = StageDirectory("two", ("a.dll", "alpha"), ("sub/b.json", "{\"k\":1}"));

        // Different content, different order on disk, and a later write — none of which may matter.
        File.SetLastWriteTimeUtc(Path.Combine(second, "a.dll"), DateTime.UtcNow.AddDays(-3));

        var packager = new ZipDirectoryPackager(ArtifactType.DotnetApplication);

        var one = await packager.PackageAsync(new PackageRequest(artifactId, identity.BuildId, first, Path.Combine(_root, "out1")));
        var two = await packager.PackageAsync(new PackageRequest(artifactId, identity.BuildId, second, Path.Combine(_root, "out2")));

        Assert.True(one.IsPackaged);
        Assert.True(two.IsPackaged);
        Assert.Equal(one.Artifact!.ContentDigest, two.Artifact!.ContentDigest);
    }

    [Fact]
    public async Task ZipPackaging_DiffersWhenContentDiffers()
    {
        var identity = BuildTestData.Identity();
        var artifactId = ArtifactId.For(identity.UnitId, ArtifactType.DotnetApplication, "marketsurvey.api", "0.1.0");
        var packager = new ZipDirectoryPackager(ArtifactType.DotnetApplication);

        var one = await packager.PackageAsync(new PackageRequest(artifactId, identity.BuildId, StageDirectory("x", ("a.dll", "alpha")), Path.Combine(_root, "ox")));
        var two = await packager.PackageAsync(new PackageRequest(artifactId, identity.BuildId, StageDirectory("y", ("a.dll", "beta")), Path.Combine(_root, "oy")));

        Assert.NotEqual(one.Artifact!.ContentDigest, two.Artifact!.ContentDigest);
    }

    [Fact]
    public async Task ZipPackaging_RefusesAnEmptyDirectory()
    {
        var identity = BuildTestData.Identity();
        var empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
        var packager = new ZipDirectoryPackager(ArtifactType.DotnetApplication);

        var outcome = await packager.PackageAsync(new PackageRequest(
            ArtifactId.For(identity.UnitId, ArtifactType.DotnetApplication, "marketsurvey.api", "0.1.0"),
            identity.BuildId, empty, Path.Combine(_root, "out")));

        Assert.False(outcome.IsPackaged);
        Assert.Contains("certifies nothing", outcome.RefusalReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZipPackaging_RefusesAMissingDirectory()
    {
        var identity = BuildTestData.Identity();
        var packager = new ZipDirectoryPackager(ArtifactType.DotnetApplication);

        var outcome = await packager.PackageAsync(new PackageRequest(
            ArtifactId.For(identity.UnitId, ArtifactType.DotnetApplication, "marketsurvey.api", "0.1.0"),
            identity.BuildId, Path.Combine(_root, "absent"), Path.Combine(_root, "out")));

        Assert.False(outcome.IsPackaged);
    }

    /// <summary>
    /// The negative obligation. The packager is given only a directory, and the test asserts the archive
    /// contains exactly the files it was given — no configuration, no endpoint, nothing ambient. The
    /// packagers in this estate's history took <c>API_INTERNAL_URL</c> as a build argument precisely because
    /// a packaging step is a convenient place to "fill in" a value.
    /// </summary>
    [Fact]
    public async Task ZipPackaging_ContainsExactlyTheStagedContent()
    {
        var identity = BuildTestData.Identity();
        var artifactId = ArtifactId.For(identity.UnitId, ArtifactType.DotnetApplication, "marketsurvey.api", "0.1.0");
        var staging = StageDirectory("exact", ("app.dll", "binary"), ("appsettings.json", "{ \"ApiKeyRef\": \"NEXUS_PROVIDER_CREDENTIAL_REF\" }"));

        var packager = new ZipDirectoryPackager(ArtifactType.DotnetApplication);
        var outcome = await packager.PackageAsync(new PackageRequest(artifactId, identity.BuildId, staging, Path.Combine(_root, "oe")));

        using var archive = System.IO.Compression.ZipFile.OpenRead(Path.Combine(_root, "oe", "marketsurvey.api.zip"));
        var names = archive.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(["app.dll", "appsettings.json"], names);
    }

    [Fact]
    public async Task PreBuiltPackaging_CopiesAndHashesWithoutRepacking()
    {
        var identity = BuildTestData.Identity();
        var source = Path.Combine(_root, "thing.nupkg");
        Directory.CreateDirectory(_root);
        File.WriteAllText(source, "an already-built package");

        var packager = new PreBuiltFilePackager();
        var outcome = await packager.PackageAsync(new PackageRequest(
            ArtifactId.For(identity.UnitId, ArtifactType.Package, "marketsurvey.contracts", "0.1.0"),
            identity.BuildId, source, Path.Combine(_root, "op")));

        Assert.True(outcome.IsPackaged);
        Assert.Equal("thing.nupkg", outcome.Artifact!.FileName);

        using var original = File.OpenRead(source);
        Assert.Equal(ArtifactDigest.Compute(original), outcome.Artifact.ContentDigest);
    }

    [Fact]
    public void ZippingPackager_RefusesATypeItCannotPackage()
    {
        Assert.Throws<ArgumentException>(() => new ZipDirectoryPackager(ArtifactType.Package));
    }
}
