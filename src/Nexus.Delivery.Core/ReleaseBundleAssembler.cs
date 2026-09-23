using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>Everything a release is assembled from. Supplied, never discovered.</summary>
public sealed record ReleaseAssemblyRequest
{
    public ReleaseAssemblyRequest(
        BuildManifest manifest,
        ArtifactDigest buildManifestDigest,
        BundleId bundleId,
        string buildManifestReference,
        IReadOnlyList<ContractVersion> contractVersions,
        MigrationAssessment migrations,
        RollbackMetadata rollback,
        HealthContract health,
        string configurationSchemaVersion,
        DateTimeOffset createdAt,
        IReadOnlyList<string>? configurationKeys = null,
        IReadOnlyList<SecretReference>? secretReferences = null,
        GovernedWorkReference? originatingWork = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(buildManifestDigest);
        ArgumentNullException.ThrowIfNull(bundleId);
        ArgumentNullException.ThrowIfNull(contractVersions);
        ArgumentNullException.ThrowIfNull(migrations);
        ArgumentNullException.ThrowIfNull(rollback);
        ArgumentNullException.ThrowIfNull(health);

        if (string.IsNullOrWhiteSpace(buildManifestReference))
        {
            throw new ArgumentException("A release assembly must name where the build manifest lives.", nameof(buildManifestReference));
        }

        Manifest = manifest;
        BuildManifestDigest = buildManifestDigest;
        BundleId = bundleId;
        BuildManifestReference = buildManifestReference;
        ContractVersions = contractVersions;
        Migrations = migrations;
        Rollback = rollback;
        Health = health;
        ConfigurationSchemaVersion = configurationSchemaVersion;
        CreatedAt = createdAt;
        ConfigurationKeys = configurationKeys ?? [];
        SecretReferences = secretReferences ?? [];
        OriginatingWork = originatingWork;
    }

    public BuildManifest Manifest { get; }

    /// <summary>
    /// The <b>certified</b> build manifest's digest.
    ///
    /// <para>
    /// Supplied rather than computed from <see cref="Manifest"/>, and that distinction is a defect this
    /// stage found by checking rather than by reasoning. When the digest was computed from the manifest in
    /// hand, a release whose manifest was <i>reconstructed</i> recorded a digest of the reconstruction while
    /// naming the real manifest as its reference — so anyone following the reference computed a different
    /// value and the evidence chain did not close. The reference and the digest must describe the same
    /// object, and only the caller knows which object that is.
    /// </para>
    /// </summary>
    public ArtifactDigest BuildManifestDigest { get; }

    public BundleId BundleId { get; }

    public string BuildManifestReference { get; }

    public IReadOnlyList<ContractVersion> ContractVersions { get; }

    public MigrationAssessment Migrations { get; }

    public RollbackMetadata Rollback { get; }

    public HealthContract Health { get; }

    public string ConfigurationSchemaVersion { get; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<string> ConfigurationKeys { get; }

    public IReadOnlyList<SecretReference> SecretReferences { get; }

    public GovernedWorkReference? OriginatingWork { get; }
}

/// <summary>The outcome of assembling a release. A refusal is an ordinary result.</summary>
public sealed record ReleaseAssemblyOutcome
{
    private ReleaseAssemblyOutcome(
        ReleaseRecord? release,
        IReadOnlyList<ReleaseRefusalReason> refusalReasons,
        IReadOnlyList<string> detail)
    {
        Release = release;
        RefusalReasons = refusalReasons;
        Detail = detail;
    }

    public ReleaseRecord? Release { get; }

    public IReadOnlyList<ReleaseRefusalReason> RefusalReasons { get; }

    public IReadOnlyList<string> Detail { get; }

    public bool IsAssembled => Release is not null;

    public bool RefusedBecause(ReleaseRefusalReason reason) => RefusalReasons.Contains(reason);

    public static ReleaseAssemblyOutcome Assembled(ReleaseRecord release)
        => new(release, [], [$"Assembled {release.ReleaseId} from {release.BuildId}."]);

    public static ReleaseAssemblyOutcome Refuse(IReadOnlyList<ReleaseRefusalReason> reasons, IReadOnlyList<string> detail)
    {
        if (reasons is null || reasons.Count == 0)
        {
            throw new ArgumentException("A refusal must name at least one typed reason.", nameof(reasons));
        }

        return new ReleaseAssemblyOutcome(null, [.. reasons.Distinct().OrderBy(r => r)], detail ?? []);
    }
}

/// <summary>
/// Assembles a Release Bundle from a certified build and the store that holds its artifact.
///
/// <para>
/// <b>It assembles; it does not build.</b> Nothing here invokes a compiler, a packager or a publish step.
/// The bytes already exist, having been produced once and certified; this reads them, verifies them against
/// the digest the build recorded, and describes them. That is the whole of the build-once rule at this
/// stage: a release that re-derived its own artifact would be a second build, and two builds are two
/// artifacts however identical they look.
/// </para>
///
/// <para>
/// <b>The store is the authority, and the manifest is the claim.</b> The artifact's identity is taken from
/// what the store holds and then compared against what the manifest said. Taking it from the manifest would
/// make the verification circular — the release would record the claim it was asked to check. A mismatch is
/// reported as a hash mismatch rather than a missing artifact, because the two demand opposite responses:
/// one is a missing input, the other means the store's contents changed.
/// </para>
/// </summary>
public sealed class ReleaseBundleAssembler
{
    private readonly IArtifactStore _store;

    public ReleaseBundleAssembler(IArtifactStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<ReleaseAssemblyOutcome> AssembleAsync(
        ReleaseAssemblyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var manifest = request.Manifest;
        var reasons = new List<ReleaseRefusalReason>();
        var detail = new List<string>();
        var artifacts = new List<ReleaseArtifactIdentity>();

        foreach (var packaged in manifest.Artifacts)
        {
            var entry = await _store.ResolveByArtifactIdAsync(packaged.ArtifactId, cancellationToken).ConfigureAwait(false);

            if (entry is null)
            {
                reasons.Add(ReleaseRefusalReason.ArtifactNotRegistered);
                detail.Add(
                    $"'{packaged.ArtifactId.Value}' is named by the build manifest but is not in the store. "
                    + "A release is assembled from registered bytes, never from a claim that they exist.");
                continue;
            }

            // The recorded digest is the store's own memory of what it holds. Comparing it first catches a
            // store entry that disagrees with the manifest before any bytes are read.
            if (entry.ContentDigest != packaged.ContentDigest)
            {
                reasons.Add(ReleaseRefusalReason.ArtifactHashMismatch);
                detail.Add(
                    $"'{packaged.ArtifactId.Value}' is recorded in the store as {entry.ContentDigest}, "
                    + $"but the build manifest records {packaged.ContentDigest}.");
                continue;
            }

            // And the bytes themselves are re-hashed. This is the check TASK 12 requires before release
            // creation: the store's memory could be stale, and only re-reading the content establishes that
            // the bytes a release names are the bytes a release will move.
            var verification = await _store
                .VerifyHashAsync(packaged.ArtifactId, packaged.ContentDigest, cancellationToken)
                .ConfigureAwait(false);

            if (!verification.IsMatch)
            {
                reasons.Add(ReleaseRefusalReason.ArtifactHashMismatch);
                detail.Add($"'{packaged.ArtifactId.Value}': {verification.Detail}");
                continue;
            }

            if (!verification.StoreContentIntact)
            {
                reasons.Add(ReleaseRefusalReason.ArtifactHashMismatch);
                detail.Add(
                    $"'{packaged.ArtifactId.Value}': the stored content matches the expected digest but not the "
                    + "store's own recorded digest, so the entry is inconsistent.");
                continue;
            }

            artifacts.Add(new ReleaseArtifactIdentity(packaged.ArtifactId, entry.ContentDigest, entry.SizeBytes));
        }

        if (reasons.Count > 0)
        {
            return ReleaseAssemblyOutcome.Refuse([.. reasons], detail);
        }

        // The version comes from the artifact, never from the request. A stage that "set the release
        // version" would break the one thing the version is for: naming the same coordinate in the store,
        // the bundle and the release reference.
        var versions = artifacts.Select(a => a.Version).Distinct(StringComparer.Ordinal).ToList();

        if (versions.Count != 1)
        {
            return ReleaseAssemblyOutcome.Refuse(
                [ReleaseRefusalReason.ReleaseBundleIncomplete],
                [$"The build produced artifacts at {versions.Count} different versions ({string.Join(", ", versions)}); "
                  + "a release names one version, so these are separate releases."]);
        }

        var identity = new ReleaseIdentity(
            manifest.Identity.UnitId,
            versions[0],
            manifest.BuildId,
            [.. manifest.Identity.Sources.Select(s => s.CommitSha)],
            IReleaseTagPolicy.RefNameFor(manifest.Identity.UnitId, versions[0]),
            artifacts);

        var evidence = new ReleaseEvidence(
            request.BuildManifestDigest,
            request.BuildManifestReference,
            manifest.Tests.SuiteName,
            manifest.Tests.Verdict,
            manifest.Tests.Total,
            manifest.Tests.Passed,
            manifest.SecretScan.Verdict,
            [.. manifest.SecretScan.ScannedSubjectLabels],
            manifest.Reproducibility.Verdict,
            manifest.Reproducibility.BuildsCompared,
            manifest.Provenance.BuilderRunId);

        var release = new ReleaseRecord(
            ReleaseRecord.CurrentSchemaVersion,
            identity,
            request.BundleId,
            evidence,
            manifest.Identity.DependencyLock,
            request.ContractVersions,
            request.Migrations,
            request.Rollback,
            request.Health,
            request.ConfigurationSchemaVersion,
            request.CreatedAt,
            request.ConfigurationKeys,
            request.SecretReferences,
            request.OriginatingWork);

        return ReleaseAssemblyOutcome.Assembled(release);
    }
}
