using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;

namespace Nexus.Delivery.Tests;

/// <summary>
/// Builders for the W9.3 release records. Kept beside the W9.1 and W9.2 builders rather than duplicating
/// them: a release is assembled FROM a build manifest, so these compose <see cref="BuildTestData"/> rather
/// than restating a build.
/// </summary>
internal static class ReleaseTestData
{
    internal const string WorkReference = "WI-09-3.1";

    internal static ReleaseArtifactIdentity ArtifactIdentity(
        DeploymentUnitId? unitId = null,
        char digestFill = 'a',
        long size = 5434178,
        string name = "marketsurvey.api",
        string version = "0.1.0",
        ArtifactType type = ArtifactType.DotnetApplication)
        => new(
            ArtifactId.For(unitId ?? BuildTestData.Unit, type, name, version),
            ArtifactDigest.Parse($"sha256:{new string(digestFill, 64)}"),
            size);

    internal static ReleaseIdentity Identity(
        string unitId = "marketsurvey.api",
        string version = "0.1.0",
        string commit = BuildTestData.CommitA,
        char digestFill = 'a',
        string? refName = null)
        => new(
            DeploymentUnitId.Parse(unitId),
            version,
            BuildTestData.Identity(unitId: unitId, commit: commit).BuildId,
            [commit],
            refName ?? $"refs/tags/release/{unitId}/{version}",
            [ArtifactIdentity(DeploymentUnitId.Parse(unitId), digestFill, version: version)]);

    internal static BundleId BundleId => Contracts.BundleId.Parse("nexus-2026.09.23-w93rel");

    internal static ReleaseEvidence Evidence(
        SecretScanVerdict scan = SecretScanVerdict.Clean,
        TestVerdict tests = TestVerdict.Passed,
        ReproducibilityVerdict reproducibility = ReproducibilityVerdict.ByteIdentical,
        int buildsCompared = 2,
        string[]? subjects = null)
        => new(
            ArtifactDigest.Parse($"sha256:{new string('f', 64)}"),
            "W9_2_BUILD_ARTIFACT/BUILD_MANIFEST.txt",
            "MarketSurvey.Tests",
            tests,
            15,
            15,
            scan,
            subjects ?? ["NEXUS/Platform", "PRT/MarketSurvey"],
            reproducibility,
            buildsCompared,
            "w9.3-proof");

    internal static HealthContract Health => new("/health/ready", "/health/live");

    internal static MigrationAssessment NoMigrations
        => MigrationAssessment.NoDatabaseMigration("Inspected the published unit for a migration assembly; none is present.");

    internal static RollbackMetadata FirstRelease
        => RollbackMetadata.NoPreviousAcceptedRelease(
            crossesMigrationBoundary: false,
            "No prior accepted release exists for this unit in any environment.");

    internal static ReleaseRecord Record(
        ReleaseIdentity? identity = null,
        ReleaseEvidence? evidence = null,
        MigrationAssessment? migrations = null,
        RollbackMetadata? rollback = null,
        IReadOnlyList<ContractVersion>? contracts = null,
        GovernedWorkReference? work = null,
        string configurationSchemaVersion = "runtime-config-v1",
        IReadOnlyList<string>? configurationKeys = null,
        IReadOnlyList<SecretReference>? secretReferences = null,
        DateTimeOffset? createdAt = null)
        => new(
            ReleaseRecord.CurrentSchemaVersion,
            identity ?? Identity(),
            BundleId,
            evidence ?? Evidence(),
            BuildTestData.Unlocked,
            contracts ?? [new ContractVersion("Nexus.ProductCore.Contracts", "0.1.0", "NEXUS/Platform")],
            migrations ?? NoMigrations,
            rollback ?? FirstRelease,
            Health,
            configurationSchemaVersion,
            createdAt ?? DateTimeOffset.Parse("2026-09-23T10:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
            configurationKeys ?? ["api"],
            secretReferences,
            work ?? new GovernedWorkReference(WorkReference, "Release bundle and immutable artifact registry", "W9"));

    /// <summary>
    /// Evidence describing a fully observed release whose only outstanding items are the two security
    /// actions. Individual tests then break exactly one member, which is what makes each refusal assertion
    /// attribute the refusal to its cause rather than to a builder that happened to be incomplete.
    /// </summary>
    internal static ReleaseGateEvidence ObservedEvidence(
        bool? credentialRotationConfirmed = false,
        bool? serverSideProtectionVerified = false)
        => new()
        {
            BuildIsCertified = true,
            SourceLineageComplete = true,
            ArtifactHashMatchesManifest = true,
            BundleIsComplete = true,
            ReleaseRefGoverned = true,
            DependencyManifestAvailable = true,
            ContractCompatibilityAcceptable = true,
            MigrationStateKnown = true,
            MigrationBackupEstablished = true,
            ConfigurationSchemaKnown = true,
            HealthDefinitionPresent = true,
            RollbackStateKnown = true,
            SecretScanPassed = true,
            ConfigurationBoundaryClean = true,
            ReproducibilityEstablished = true,
            TestsPassed = true,
            CredentialRotationConfirmed = credentialRotationConfirmed,
            ReleaseRefServerSideProtectionVerified = serverSideProtectionVerified,
            Authorization = DeploymentAuthorization.For(DeploymentAuthorityRole.Owner, "owner", DateTimeOffset.UnixEpoch)
        };

    /// <summary>An evidence set with every technical condition met AND both security actions done.</summary>
    internal static ReleaseGateEvidence FullyClearedEvidence()
        => ObservedEvidence(credentialRotationConfirmed: true, serverSideProtectionVerified: true);

    /// <summary>
    /// A store with one published artifact whose bytes really hash to the digest it claims.
    ///
    /// <para>
    /// The content is generated, not a literal, for the reason <see cref="TestData.RandomAlnum"/> records:
    /// this repository's own secret-scan gate reads this tree.
    /// </para>
    /// </summary>
    internal static async Task<PublishedFixture> PublishedArtifactAsync(string root, char digestFill = 'a')
    {
        var content = System.Text.Encoding.UTF8.GetBytes("nexus-w9.3-fixture-" + TestData.RandomAlnum(24));
        var digest = ArtifactDigest.Compute(content);

        // The manifest's artifact digest must be the real one, so the fixture's identity is derived from the
        // content rather than chosen — the same discipline the production path uses.
        var identity = BuildTestData.Identity();
        var artifact = new PackagedArtifact(
            ArtifactId.For(identity.UnitId, ArtifactType.DotnetApplication, "marketsurvey.api", "0.1.0"),
            digest,
            content.Length,
            identity.BuildId,
            "marketsurvey.api.zip");

        var manifest = new BuildManifest(
            BuildManifest.CurrentSchemaVersion,
            identity,
            [artifact],
            BuildTestData.GreenTests(),
            BuildTestData.CleanScan("NEXUS/Platform", "PRT/MarketSurvey"),
            BuildTestData.Identical(digest),
            BuildTestData.Provenance,
            DateTimeOffset.Parse("2026-09-22T18:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
            ["api"]);

        var store = new FileArtifactStore(root);
        var contentPath = Path.Combine(root, "incoming-" + Guid.NewGuid().ToString("N") + ".bin");
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(contentPath, content);

        var outcome = await store.PublishAsync(artifact, contentPath);
        File.Delete(contentPath);

        return new PublishedFixture(store, artifact, manifest, digest, outcome);
    }

    internal sealed record PublishedFixture(
        IArtifactStore Store,
        PackagedArtifact Artifact,
        BuildManifest Manifest,
        ArtifactDigest Digest,
        ArtifactPublishOutcome Outcome);
}

/// <summary>Creates a temp directory that is deleted when the test ends.</summary>
internal sealed class TempRoot : IDisposable
{
    public TempRoot(string purpose)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"nexus-w93-{purpose}-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
