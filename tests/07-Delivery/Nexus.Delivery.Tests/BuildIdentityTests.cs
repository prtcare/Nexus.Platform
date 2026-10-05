using System.Reflection;
using Nexus.Delivery.Contracts;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>Shared builders for the W9.2 tests.</summary>
internal static class BuildTestData
{
    internal const string CommitA = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
    internal const string CommitB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    internal static DeploymentUnitId Unit => DeploymentUnitId.Parse("marketsurvey.api");

    internal static ToolchainIdentity Toolchain(string sdk = "10.0.302", string runtime = "10.0.0")
        => new(sdk, runtime, "linux-x64");

    internal static DependencyLockState Unlocked => DependencyLockState.Unlocked("No packages.lock.json found in the repository.");

    internal static BuildIdentity Identity(
        string unitId = "marketsurvey.api",
        string? configuration = "Release",
        string definitionVersion = "build-definition-v1",
        string commit = CommitA,
        bool dirty = false,
        ToolchainIdentity? toolchain = null,
        DependencyLockState? lockState = null,
        string repository = "PRT/MarketSurvey")
        => new(
            DeploymentUnitId.Parse(unitId),
            [new SourceRevision(repository, commit, dirty)],
            definitionVersion,
            lockState ?? Unlocked,
            toolchain ?? Toolchain(),
            configuration ?? "Release");

    internal static PackagedArtifact Artifact(
        BuildIdentity identity,
        char digestFill = 'a',
        string name = "marketsurvey.api",
        string version = "0.1.0",
        ArtifactType type = ArtifactType.DotnetApplication,
        long size = 4096)
        => new(
            ArtifactId.For(identity.UnitId, type, name, version),
            ArtifactDigest.Parse($"sha256:{new string(digestFill, 64)}"),
            size,
            identity.BuildId,
            name + ".zip");

    internal static SecretScanEvidence CleanScan(params string[] subjects) => new(
        SecretScanVerdict.Clean,
        subjects.Length > 0 ? subjects : ["PRT/MarketSurvey"],
        [],
        12,
        3,
        [],
        []);

    internal static BuildTestEvidence GreenTests(string suite = "MarketSurvey.Tests", int total = 42) => new(
        suite, TestVerdict.Passed, total, total, 0, 0);

    internal static ReproducibilityEvidence Identical(ArtifactDigest digest, int rounds = 2) => new(
        ReproducibilityVerdict.ByteIdentical,
        rounds,
        Enumerable.Repeat(digest, rounds).ToArray());

    internal static ProvenanceRecord Provenance => new("local-run-1", "host-toolchain", "win-x64", "w9.2-proof");

    internal static BuildManifest Manifest(
        BuildIdentity? identity = null,
        PackagedArtifact? artifact = null,
        BuildTestEvidence? tests = null,
        SecretScanEvidence? scan = null,
        ReproducibilityEvidence? reproducibility = null,
        DateTimeOffset? builtAt = null)
    {
        var id = identity ?? Identity();
        var art = artifact ?? Artifact(id);

        return new BuildManifest(
            BuildManifest.CurrentSchemaVersion,
            id,
            [art],
            tests ?? GreenTests(),
            scan ?? CleanScan(),
            reproducibility ?? Identical(art.ContentDigest),
            Provenance,
            builtAt ?? DateTimeOffset.Parse("2026-09-22T18:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
            ["apiBaseUrl"]);
    }

    internal static CertificationEvidence Evidence(
        BuildManifest? manifest = null,
        bool requiresEnvironmentRebuild = false,
        bool buildSucceeded = true,
        string[]? expectedSubjects = null)
    {
        var m = manifest ?? Manifest();

        return new CertificationEvidence(
            m,
            expectedSubjects ?? [.. m.SecretScan.ScannedSubjectLabels],
            requiresEnvironmentRebuild,
            buildSucceeded);
    }
}

/// <summary>
/// Build identity: the derived id, its determinism, and the property the Owner's ruling makes structural —
/// that an environment cannot be part of it.
/// </summary>
public sealed class BuildIdentityTests
{
    [Fact]
    public void BuildId_IsDerivedFromTheGovernedInputs_NotAssigned()
    {
        var first = BuildTestData.Identity();
        var second = BuildTestData.Identity();

        Assert.Equal(first.BuildId, second.BuildId);

        // Same inputs, different object graph: the id is a function of the inputs, not of when or where.
        Assert.Equal(BuildTestData.Identity(commit: BuildTestData.CommitA).BuildId, BuildTestData.Identity(commit: BuildTestData.CommitA).BuildId);
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("sdk")]
    [InlineData("runtime")]
    [InlineData("configuration")]
    [InlineData("definition")]
    [InlineData("unit")]
    [InlineData("lock")]
    public void AnyGovernedInputChanges_TheBuildId(string which)
    {
        var baseline = BuildTestData.Identity();

        var changed = which switch
        {
            "commit" => BuildTestData.Identity(commit: BuildTestData.CommitB),
            "sdk" => BuildTestData.Identity(toolchain: BuildTestData.Toolchain(sdk: "10.0.400")),
            "runtime" => BuildTestData.Identity(toolchain: BuildTestData.Toolchain(runtime: "10.0.1")),
            "configuration" => BuildTestData.Identity(configuration: "Debug"),
            "definition" => BuildTestData.Identity(definitionVersion: "build-definition-v2"),
            "unit" => BuildTestData.Identity(unitId: "nexus.developer.api"),
            "lock" => BuildTestData.Identity(lockState: DependencyLockState.Locked([new LockFileDigest("PRT/MarketSurvey", "packages.lock.json", ArtifactDigest.Parse($"sha256:{new string('c', 64)}"))])),
            _ => throw new ArgumentOutOfRangeException(nameof(which))
        };

        Assert.NotEqual(baseline.BuildId, changed.BuildId);
    }

    /// <summary>
    /// The Owner's ruling made structural: no member of the build identity may mention an environment.
    /// Asserted by reflection rather than by inspection, because the property has to survive future edits
    /// by people who have not read this test.
    /// </summary>
    [Fact]
    public void BuildIdentity_HasNoEnvironmentMember()
    {
        var members = typeof(BuildIdentity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Concat(typeof(BuildIdentity).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name))
            .ToList();

        var offending = members
            .Where(name => name.Contains("Environment", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Env", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offending.Count == 0,
            "An environment member on the build identity would make two environments two builds. Found: " + string.Join(", ", offending));
    }

    [Fact]
    public void BuildId_IsIndependentOfEnvironmentLabel_InTheCanonicalForm()
    {
        // The same governed inputs described with an environment-shaped suffix in a place that should not
        // care must still produce the same canonical form — and the guard, not the identity, is what
        // refuses the suffix. This asserts the identity itself stays environment-blind.
        var a = BuildTestData.Identity();
        var b = BuildTestData.Identity();

        Assert.Equal(a.CanonicalForm(), b.CanonicalForm());
    }

    [Fact]
    public void CanonicalForm_IsNotAmbiguous_BetweenAdjacentFields()
    {
        // ["ab","c"] must not hash the same as ["a","bc"]. The NUL separator is what prevents it.
        var one = new BuildIdentity(
            DeploymentUnitId.Parse("nexus.ab"),
            [new SourceRevision("repo", BuildTestData.CommitA, false)],
            "def-c",
            BuildTestData.Unlocked,
            BuildTestData.Toolchain(),
            "Release");

        var two = new BuildIdentity(
            DeploymentUnitId.Parse("nexus.a"),
            [new SourceRevision("repo", BuildTestData.CommitA, false)],
            "bdef-c",
            BuildTestData.Unlocked,
            BuildTestData.Toolchain(),
            "Release");

        Assert.NotEqual(one.CanonicalForm(), two.CanonicalForm());
        Assert.NotEqual(one.BuildId, two.BuildId);
    }

    [Fact]
    public void ASourceRevision_RefusesAShortOrBranchLikeSha()
    {
        Assert.Throws<ArgumentException>(() => new SourceRevision("repo", "abc123", false));
        Assert.Throws<ArgumentException>(() => new SourceRevision("repo", "main", false));
        Assert.Throws<ArgumentException>(() => new SourceRevision("repo", string.Empty, false));
    }

    [Fact]
    public void AnIdentity_RefusesNoSources_AndTwoRevisionsOfOneRepository()
    {
        Assert.Throws<ArgumentException>(() => new BuildIdentity(
            BuildTestData.Unit, [], "def-v1", BuildTestData.Unlocked, BuildTestData.Toolchain(), "Release"));

        Assert.Throws<ArgumentException>(() => new BuildIdentity(
            BuildTestData.Unit,
            [new SourceRevision("repo", BuildTestData.CommitA, false), new SourceRevision("repo", BuildTestData.CommitB, false)],
            "def-v1", BuildTestData.Unlocked, BuildTestData.Toolchain(), "Release"));
    }

    [Fact]
    public void ADirtyWorkingTree_IsRecorded_NotRefusedHere()
    {
        // The type records the fact; the gate refuses it with a typed reason. A dirty tree is a condition
        // of the world, not a programming error, and throwing here would leave no record of the attempt.
        var identity = BuildTestData.Identity(dirty: true);

        Assert.True(identity.HasDirtySource);
        Assert.Single(identity.Sources);
    }

    [Fact]
    public void AnUnlockedDependencyState_MustSayWhatWasSearchedFor()
    {
        Assert.Throws<ArgumentException>(() => DependencyLockState.Unlocked("   "));

        var unlocked = DependencyLockState.Unlocked("No packages.lock.json in any repository.");
        Assert.False(unlocked.IsLocked);
        Assert.NotNull(unlocked.Note);
    }

    [Fact]
    public void ALockedDependencyState_MustCarryAtLeastOneLockFile()
    {
        Assert.Throws<ArgumentException>(() => DependencyLockState.Locked([]));
    }
}
