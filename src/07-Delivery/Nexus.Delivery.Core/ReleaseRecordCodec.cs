using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The canonical wire form of a <see cref="ReleaseRecord"/>.
///
/// <para>
/// <b>Why a codec and not a serializer call.</b> The registry's immutability rule compares a digest
/// computed from these bytes, so the bytes must be identical for identical releases — on any machine, in
/// any process, in any order. A default serializer's output is a function of property discovery order,
/// dictionary enumeration order and culture; each is stable in practice and none of them is guaranteed, and
/// a digest that depended on any of them would silently stop proving immutability. So the encoding is
/// canonicalised here explicitly, exactly as <see cref="BundleManifestCodec"/> does one level down: every
/// collection is sorted, every timestamp is round-tripped through its invariant form, and the DTO shape is
/// owned by this file.
/// </para>
///
/// <para>
/// <b>What is NOT in the release.</b> No configuration value, no secret value, no environment. Configuration
/// appears as key names, secrets as reference names, and <see cref="SecretReference"/> makes a value
/// unrepresentable at construction — so this codec could not encode one even if a caller wanted it to.
/// </para>
/// </summary>
public static class ReleaseRecordCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Encodes a release to its canonical byte form. Deterministic for equal releases.</summary>
    public static byte[] Encode(ReleaseRecord release)
    {
        ArgumentNullException.ThrowIfNull(release);

        var identity = release.Identity;

        var dto = new ReleaseDto(
            ReleaseRecord.CurrentSchemaVersion,
            new IdentityDto(
                identity.UnitId.Value,
                identity.Version,
                identity.BuildId.Value,
                identity.ReleaseId.Value,
                identity.ReleaseRefName,
                [.. identity.SourceCommits],
                [.. identity.Artifacts
                    .OrderBy(a => a.ArtifactId.Value, StringComparer.Ordinal)
                    .Select(a => new ArtifactDto(a.ArtifactId.Value, a.ContentDigest.ToString(), a.SizeBytes))]),
            release.BundleId.Value,
            new EvidenceDto(
                release.Evidence.BuildManifestDigest.ToString(),
                release.Evidence.BuildManifestReference,
                release.Evidence.TestSuiteName,
                release.Evidence.TestVerdict.ToString(),
                release.Evidence.TestsTotal,
                release.Evidence.TestsPassed,
                release.Evidence.SecretScanVerdict.ToString(),
                [.. release.Evidence.SecretScanSubjectLabels.OrderBy(l => l, StringComparer.Ordinal)],
                release.Evidence.ReproducibilityVerdict.ToString(),
                release.Evidence.BuildsCompared,
                release.Evidence.BuilderRunId),
            new DependencyDto(
                release.DependencyLock.IsLocked,
                release.DependencyLock.Note,
                [.. release.DependencyLock.LockFiles
                    .OrderBy(l => l.RepositoryLabel, StringComparer.Ordinal)
                    .ThenBy(l => l.RelativePath, StringComparer.Ordinal)
                    .Select(l => new LockFileDto(l.RepositoryLabel, l.RelativePath, l.Digest.ToString()))]),
            [.. release.ContractVersions
                .OrderBy(c => c.ContractName, StringComparer.Ordinal)
                .ThenBy(c => c.Version, StringComparer.Ordinal)
                .Select(c => new ContractDto(c.ContractName, c.Version, c.SourceRepository))],
            ToDto(release.Migrations),
            ToDto(release.Rollback),
            new HealthDto(release.Health.ReadinessPath, release.Health.LivenessPath),
            release.ConfigurationSchemaVersion,
            release.CreatedAt.ToString("O"),
            [.. release.ConfigurationKeys.OrderBy(k => k, StringComparer.Ordinal)],
            [.. release.SecretReferences.OrderBy(s => s.Value, StringComparer.Ordinal).Select(s => s.Value)],
            release.OriginatingWork is null
                ? null
                : new WorkDto(release.OriginatingWork.WorkReference, release.OriginatingWork.Title, release.OriginatingWork.Series));

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto, Options));
    }

    // Deliberately no digest here. This codec renders a release for STORAGE; what a release MEANS is
    // ReleaseRecord.CanonicalForm, and ReleaseRecord.ComputeRecordDigest is the one definition of its
    // digest. An earlier draft of this file offered a second, encoding-over-the-JSON digest, and the
    // registry enforced one while the release reference's annotation recorded the other — two values for
    // one concept, which is the drift this estate has recorded three times now. The storage rendering and
    // the identity are different things and only one of them is a digest.

    /// <summary>
    /// Decodes a release. Returns false on anything malformed rather than throwing, so a registry can report
    /// a corrupt entry as a refusal with a reason instead of failing the caller's process.
    ///
    /// <para>
    /// <b>The release id is re-derived and compared, not trusted.</b> A stored id that disagreed with the
    /// identity it was stored beside would make the registry's lookup key mean something different from the
    /// record it returns, which is the one inconsistency that would defeat the immutability rule from the
    /// inside. A mismatch is treated as a corrupt record.
    /// </para>
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> encoded, out ReleaseRecord? release)
    {
        release = null;

        ReleaseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ReleaseDto>(encoded, Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (dto?.Identity is null || dto.Identity.Artifacts is null || dto.Identity.Artifacts.Length == 0)
        {
            return false;
        }

        try
        {
            var artifacts = dto.Identity.Artifacts
                .Select(a => new ReleaseArtifactIdentity(
                    Contracts.ArtifactId.Parse(a.ArtifactId),
                    Contracts.ArtifactDigest.Parse(a.Digest),
                    a.SizeBytes))
                .ToArray();

            var identity = new ReleaseIdentity(
                Contracts.DeploymentUnitId.Parse(dto.Identity.UnitId),
                dto.Identity.Version,
                Contracts.BuildId.Parse(dto.Identity.BuildId),
                dto.Identity.SourceCommits ?? [],
                dto.Identity.ReleaseRefName,
                artifacts);

            if (!string.Equals(identity.ReleaseId.Value, dto.Identity.ReleaseId, StringComparison.Ordinal))
            {
                return false;
            }

            var evidence = new ReleaseEvidence(
                Contracts.ArtifactDigest.Parse(dto.Evidence.BuildManifestDigest),
                dto.Evidence.BuildManifestReference,
                dto.Evidence.TestSuite,
                Enum.Parse<TestVerdict>(dto.Evidence.TestVerdict),
                dto.Evidence.TestsTotal,
                dto.Evidence.TestsPassed,
                Enum.Parse<SecretScanVerdict>(dto.Evidence.SecretScanVerdict),
                dto.Evidence.SecretScanSubjects ?? [],
                Enum.Parse<ReproducibilityVerdict>(dto.Evidence.Reproducibility),
                dto.Evidence.BuildsCompared,
                dto.Evidence.BuilderRunId);

            var dependencyLock = dto.Dependency.IsLocked
                ? DependencyLockState.Locked([.. (dto.Dependency.LockFiles ?? [])
                    .Select(l => new LockFileDigest(l.RepositoryLabel, l.RelativePath, Contracts.ArtifactDigest.Parse(l.Digest)))])
                : DependencyLockState.Unlocked(dto.Dependency.Note ?? "(not recorded)");

            var contracts = (dto.Contracts ?? [])
                .Select(c => new ContractVersion(c.ContractName, c.Version, c.SourceRepository))
                .ToArray();

            var health = new HealthContract(dto.Health.ReadinessPath, dto.Health.LivenessPath);

            var work = dto.OriginatingWork is null
                ? null
                : new GovernedWorkReference(dto.OriginatingWork.WorkReference, dto.OriginatingWork.Title, dto.OriginatingWork.Series);

            release = new ReleaseRecord(
                dto.SchemaVersion,
                identity,
                Contracts.BundleId.Parse(dto.BundleId),
                evidence,
                dependencyLock,
                contracts,
                FromDto(dto.Migrations),
                FromDto(dto.Rollback),
                health,
                dto.ConfigurationSchemaVersion,
                DateTimeOffset.Parse(dto.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                dto.ConfigurationKeys ?? [],
                [.. (dto.SecretReferences ?? []).Select(SecretReference.Parse)],
                work);

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or JsonException)
        {
            return false;
        }
    }

    private static MigrationDto ToDto(MigrationAssessment assessment) => new(
        assessment.State.ToString(),
        assessment.Metadata.Provider,
        [.. assessment.Metadata.MigrationIds],
        assessment.FromVersion,
        assessment.ToVersion,
        assessment.Compatibility?.ToString(),
        assessment.BackupRequired,
        assessment.Reversibility.ToString(),
        assessment.Basis);

    private static MigrationAssessment FromDto(MigrationDto dto)
    {
        var state = Enum.Parse<MigrationRequirementState>(dto.State);

        return state switch
        {
            MigrationRequirementState.NoDatabaseMigration =>
                MigrationAssessment.NoDatabaseMigration(dto.Basis),

            MigrationRequirementState.MigrationsRequired =>
                MigrationAssessment.Required(
                    new MigrationMetadata(dto.Provider, dto.MigrationIds ?? []),
                    dto.FromVersion,
                    dto.ToVersion,
                    // Absent is ABSENT, never Match. This defaulted to `Match` until W9.4, and the consequence
                    // was measured on the first real release: the registered record does not record a
                    // compatibility, the decoder invented the permissive verdict, the record's canonical form
                    // (which projects this field) therefore differed, and the registry reported an intact
                    // release as "the registry's own contents have changed". Two defects in one default —
                    // a false corruption finding, and a decode that claims the only compatibility verdict
                    // which permits a migration boundary to be crossed without investigation.
                    dto.Compatibility is null ? null : Enum.Parse<MigrationCompatibility>(dto.Compatibility),
                    dto.BackupRequired,
                    Enum.Parse<MigrationReversibility>(dto.Reversibility),
                    dto.Basis),

            _ => MigrationAssessment.Unestablished(
                new MigrationMetadata(dto.Provider, dto.MigrationIds ?? []),
                dto.Basis)
        };
    }

    private static RollbackDto ToDto(RollbackMetadata rollback) => new(
        rollback.State.ToString(),
        rollback.PreviousReleaseId?.Value,
        rollback.CrossesMigrationBoundary,
        rollback.Rehearsed,
        rollback.Basis);

    private static RollbackMetadata FromDto(RollbackDto dto)
        => Enum.Parse<RollbackReferenceState>(dto.State) == RollbackReferenceState.NoPreviousAcceptedRelease
            ? RollbackMetadata.NoPreviousAcceptedRelease(dto.CrossesMigrationBoundary, dto.Basis)
            : RollbackMetadata.ToPreviousRelease(
                Contracts.ReleaseId.Parse(dto.PreviousReleaseId
                    ?? throw new JsonException("A rollback reference that names a previous release must carry its id.")),
                dto.CrossesMigrationBoundary,
                dto.Rehearsed,
                dto.Basis);

    private sealed record ReleaseDto(
        [property: JsonPropertyOrder(0)] int SchemaVersion,
        [property: JsonPropertyOrder(1)] IdentityDto Identity,
        [property: JsonPropertyOrder(2)] string BundleId,
        [property: JsonPropertyOrder(3)] EvidenceDto Evidence,
        [property: JsonPropertyOrder(4)] DependencyDto Dependency,
        [property: JsonPropertyOrder(5)] ContractDto[]? Contracts,
        [property: JsonPropertyOrder(6)] MigrationDto Migrations,
        [property: JsonPropertyOrder(7)] RollbackDto Rollback,
        [property: JsonPropertyOrder(8)] HealthDto Health,
        [property: JsonPropertyOrder(9)] string ConfigurationSchemaVersion,
        [property: JsonPropertyOrder(10)] string CreatedAt,
        [property: JsonPropertyOrder(11)] string[]? ConfigurationKeys,
        [property: JsonPropertyOrder(12)] string[]? SecretReferences,
        [property: JsonPropertyOrder(13)] WorkDto? OriginatingWork);

    private sealed record IdentityDto(
        [property: JsonPropertyOrder(0)] string UnitId,
        [property: JsonPropertyOrder(1)] string Version,
        [property: JsonPropertyOrder(2)] string BuildId,
        [property: JsonPropertyOrder(3)] string ReleaseId,
        [property: JsonPropertyOrder(4)] string ReleaseRefName,
        [property: JsonPropertyOrder(5)] string[]? SourceCommits,
        [property: JsonPropertyOrder(6)] ArtifactDto[]? Artifacts);

    private sealed record ArtifactDto(
        [property: JsonPropertyOrder(0)] string ArtifactId,
        [property: JsonPropertyOrder(1)] string Digest,
        [property: JsonPropertyOrder(2)] long SizeBytes);

    private sealed record EvidenceDto(
        [property: JsonPropertyOrder(0)] string BuildManifestDigest,
        [property: JsonPropertyOrder(1)] string BuildManifestReference,
        [property: JsonPropertyOrder(2)] string TestSuite,
        [property: JsonPropertyOrder(3)] string TestVerdict,
        [property: JsonPropertyOrder(4)] int TestsTotal,
        [property: JsonPropertyOrder(5)] int TestsPassed,
        [property: JsonPropertyOrder(6)] string SecretScanVerdict,
        [property: JsonPropertyOrder(7)] string[]? SecretScanSubjects,
        [property: JsonPropertyOrder(8)] string Reproducibility,
        [property: JsonPropertyOrder(9)] int BuildsCompared,
        [property: JsonPropertyOrder(10)] string BuilderRunId);

    private sealed record DependencyDto(
        [property: JsonPropertyOrder(0)] bool IsLocked,
        [property: JsonPropertyOrder(1)] string? Note,
        [property: JsonPropertyOrder(2)] LockFileDto[]? LockFiles);

    private sealed record LockFileDto(
        [property: JsonPropertyOrder(0)] string RepositoryLabel,
        [property: JsonPropertyOrder(1)] string RelativePath,
        [property: JsonPropertyOrder(2)] string Digest);

    private sealed record ContractDto(
        [property: JsonPropertyOrder(0)] string ContractName,
        [property: JsonPropertyOrder(1)] string Version,
        [property: JsonPropertyOrder(2)] string? SourceRepository);

    private sealed record MigrationDto(
        [property: JsonPropertyOrder(0)] string State,
        [property: JsonPropertyOrder(1)] string Provider,
        [property: JsonPropertyOrder(2)] string[]? MigrationIds,
        [property: JsonPropertyOrder(3)] string? FromVersion,
        [property: JsonPropertyOrder(4)] string? ToVersion,
        [property: JsonPropertyOrder(5)] string? Compatibility,
        [property: JsonPropertyOrder(6)] bool BackupRequired,
        [property: JsonPropertyOrder(7)] string Reversibility,
        [property: JsonPropertyOrder(8)] string Basis);

    private sealed record RollbackDto(
        [property: JsonPropertyOrder(0)] string State,
        [property: JsonPropertyOrder(1)] string? PreviousReleaseId,
        [property: JsonPropertyOrder(2)] bool CrossesMigrationBoundary,
        [property: JsonPropertyOrder(3)] bool Rehearsed,
        [property: JsonPropertyOrder(4)] string Basis);

    private sealed record HealthDto(
        [property: JsonPropertyOrder(0)] string ReadinessPath,
        [property: JsonPropertyOrder(1)] string? LivenessPath);

    private sealed record WorkDto(
        [property: JsonPropertyOrder(0)] string WorkReference,
        [property: JsonPropertyOrder(1)] string? Title,
        [property: JsonPropertyOrder(2)] string? Series);
}
