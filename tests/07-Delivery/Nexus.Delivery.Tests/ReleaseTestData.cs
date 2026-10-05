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
        ArtifactType type = ArtifactType.DotnetApplication,
        ArtifactDigest? digest = null)
        => new(
            ArtifactId.For(unitId ?? BuildTestData.Unit, type, name, version),
            digest ?? ArtifactDigest.Parse($"sha256:{new string(digestFill, 64)}"),
            size);

    internal static ReleaseIdentity Identity(
        string unitId = "marketsurvey.api",
        string version = "0.1.0",
        string commit = BuildTestData.CommitA,
        char digestFill = 'a',
        string? refName = null,
        ArtifactDigest? artifactDigest = null,
        long artifactSize = 5434178)
        => new(
            DeploymentUnitId.Parse(unitId),
            version,
            BuildTestData.Identity(unitId: unitId, commit: commit).BuildId,
            [commit],
            refName ?? $"refs/tags/release/{unitId}/{version}",
            [ArtifactIdentity(DeploymentUnitId.Parse(unitId), digestFill, artifactSize, version: version, digest: artifactDigest)]);

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
    /// <param name="credentialRotationConfirmed">C-1. Defaults to <c>false</c> — recorded as not yet done.</param>
    /// <param name="referenceProtection">
    /// C-2, as a mechanism rather than a boolean. Defaults to the deviation recorded but <b>not yet verified
    /// against this release</b>, which is the state that leaves a security action outstanding.
    /// </param>
    internal static ReleaseGateEvidence ObservedEvidence(
        bool? credentialRotationConfirmed = false,
        ReleaseReferenceProtectionEvidence? referenceProtection = null)
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
            ReleaseReferenceProtection = referenceProtection
                ?? ReleaseReferenceProtectionEvidence.CompensatingControl(verified: false),
            Authorization = DeploymentAuthorization.For(DeploymentAuthorityRole.Owner, "owner", DateTimeOffset.UnixEpoch)
        };

    /// <summary>
    /// An evidence set with every technical condition met AND both security actions done, <b>as this estate
    /// actually satisfies them</b>: rotation confirmed by the Owner (C-1), and the release reference's
    /// protection established by the Owner-approved C-2 compensating control (C-2) in place of the server-side
    /// ruleset that cannot be installed on these repositories.
    ///
    /// <para>
    /// It deliberately does <b>not</b> say <c>ServerSideProtected</c>. Nothing here has observed a ruleset, and
    /// a fixture that recorded one would be the exact falsehood this model was built to remove — as well as
    /// making every test that uses it pass in ENV-PROD, where the deviation does not reach.
    /// </para>
    /// </summary>
    internal static ReleaseGateEvidence FullyClearedEvidence()
        => ObservedEvidence(credentialRotationConfirmed: true, referenceProtection: CompensatingControl());

    /// <summary>The Owner-approved C-2 compensating control, approved and verified, scoped to DEV/TEST.</summary>
    internal static ReleaseReferenceProtectionEvidence CompensatingControl()
        => ReleaseReferenceProtectionEvidence.CompensatingControl();

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

/// <summary>
/// An in-memory <see cref="IReleaseRegistry"/> that models the one property the publisher depends on: a
/// ReleaseId either is registered, with the record digest it was registered with, or it is not.
///
/// <para>
/// <b>Why a double rather than the real <c>FileReleaseRegistry</c>.</b> The real registry is exercised by its
/// own suite, including its immutability refusal. What the publisher needs from it here is an answer to
/// "does this ReleaseId already exist, and with which digest", and that answer must be settable to null —
/// which no correctly-used real registry can produce for a record it was just handed. Rule 1's whole content
/// is the difference between registered and not, so a fixture that could only ever be registered could not
/// test it.
/// </para>
///
/// <para>
/// It still refuses a re-registration with different content rather than overwriting, so a test cannot
/// accidentally build a state the real system would have refused to create.
/// </para>
/// </summary>
internal sealed class FakeReleaseRegistry : IReleaseRegistry
{
    private readonly Dictionary<string, (ArtifactDigest Digest, ReleaseLifecycleState Lifecycle)> _entries =
        new(StringComparer.Ordinal);

    /// <summary>Register a release as the real registry would, immutability refusal included.</summary>
    public void Register(ReleaseRecord release, ReleaseLifecycleState lifecycle = ReleaseLifecycleState.ReleaseCertified)
    {
        var id = release.ReleaseId.Value;
        var digest = release.ComputeRecordDigest();

        if (_entries.TryGetValue(id, out var existing) && existing.Digest != digest)
        {
            throw new InvalidOperationException(
                $"{id} is already registered with different content. A fixture must not create a state the registry refuses.");
        }

        _entries[id] = (digest, lifecycle);
    }

    /// <summary>Register a digest that is deliberately NOT the presented record's, to model a drifted registry.</summary>
    public void RegisterWithDigest(ReleaseRecord release, ArtifactDigest digest) =>
        _entries[release.ReleaseId.Value] = (digest, ReleaseLifecycleState.ReleaseCertified);

    public Task<ReleaseRegistrationOutcome> RegisterAsync(ReleaseRecord release, CancellationToken cancellationToken = default)
    {
        var id = release.ReleaseId.Value;
        var digest = release.ComputeRecordDigest();
        var alreadyPresent = _entries.ContainsKey(id);

        if (alreadyPresent && _entries[id].Digest != digest)
        {
            return Task.FromResult(ReleaseRegistrationOutcome.Refused(
                ReleaseRegistryRefusalReason.ReleaseIdExistsWithDifferentContent,
                $"{id} is already registered with different content."));
        }

        _entries[id] = (digest, ReleaseLifecycleState.ReleaseCertified);
        return Task.FromResult(ReleaseRegistrationOutcome.Accepted(EntryFor(release), alreadyPresent));
    }

    public Task<bool> ExistsAsync(ReleaseId releaseId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.ContainsKey(releaseId.Value));

    public Task<ReleaseRecord?> TryOpenAsync(ReleaseId releaseId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ReleaseRecord?>(null);

    public Task<ReleaseRegistryEntry?> TryGetEntryAsync(ReleaseId releaseId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.TryGetValue(releaseId.Value, out var entry)
            ? new ReleaseRegistryEntry(
                releaseId,
                entry.Digest,
                entry.Lifecycle,
                DateTimeOffset.UnixEpoch,
                BuildTestData.Unit,
                "0.1.0",
                BuildTestData.Identity().BuildId,
                ReleaseTestData.BundleId,
                $"refs/tags/release/{BuildTestData.Unit.Value}/0.1.0")
            : null);

    public Task<ReleaseVerification> VerifyAsync(ReleaseId releaseId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The publisher does not verify records; the registry's own suite covers that.");

    public Task<ReleaseRegistryEntry> SetLifecycleAsync(
        ReleaseId releaseId,
        ReleaseLifecycleState state,
        string reason,
        ReleaseId? supersededBy = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The publisher does not move lifecycle state.");

    public Task<IReadOnlyList<ReleaseRegistryEntry>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReleaseRegistryEntry>>([]);

    private static ReleaseRegistryEntry EntryFor(ReleaseRecord release) =>
        new(
            release.ReleaseId,
            release.ComputeRecordDigest(),
            ReleaseLifecycleState.ReleaseCertified,
            DateTimeOffset.UnixEpoch,
            release.Identity.UnitId,
            release.Identity.Version,
            release.Identity.BuildId,
            release.BundleId,
            release.Identity.ReleaseRefName);
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
