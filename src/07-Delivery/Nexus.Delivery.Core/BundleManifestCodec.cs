using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The canonical wire form of a <see cref="ReleaseBundle"/>.
///
/// <para>
/// <b>Why a codec rather than a serializer call.</b> The manifest's digest is computed from these
/// bytes, so the bytes must be identical for identical bundles — on any machine, in any process, in
/// any order. A default serializer's output is a function of property discovery order, dictionary
/// enumeration order and culture; those are all stable in practice and none of them is guaranteed,
/// and a digest that depends on any of them would silently stop proving identity. So the encoding is
/// canonicalised here, explicitly: every collection is sorted, every timestamp is round-tripped
/// through its invariant form, and the shape is a DTO owned by this file.
/// </para>
///
/// <para>
/// <b>What is NOT in the manifest.</b> No configuration value, no secret value. Configuration appears
/// as key names, secrets as reference names — and <see cref="SecretReference"/> makes a value
/// unrepresentable at the point of construction, so this codec could not encode one even if a caller
/// wanted it to.
/// </para>
/// </summary>
public static class BundleManifestCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Encodes a bundle to its canonical byte form. Deterministic for equal bundles.</summary>
    public static byte[] Encode(ReleaseBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var dto = new ManifestDto(
            bundle.SchemaVersion,
            bundle.BundleId.Value,
            bundle.CreatedAt.ToString("O"),
            new SourceDto(bundle.Source.CommitSha, bundle.Source.RefName, bundle.Source.WorkingTreeIsDirty),
            new BuilderDto(bundle.Builder.SdkVersion, bundle.Builder.PipelineRunId, bundle.Builder.HostOperatingSystem, bundle.Builder.BuilderImage),
            [.. bundle.Artifacts.OrderBy(a => a.UnitId.Value, StringComparer.Ordinal).Select(ToDto)],
            [.. bundle.SharedContracts.OrderBy(a => a.UnitId.Value, StringComparer.Ordinal).Select(ToDto)],
            [.. bundle.Migrations.OrderBy(m => m.UnitId.Value, StringComparer.Ordinal)
                .Select(m => new MigrationDto(m.UnitId.Value, m.Migrations.Provider, m.Migrations.SetDigest.ToString(), [.. m.Migrations.MigrationIds]))],
            [.. bundle.ConfigurationKeys
                .SelectMany(kvp => kvp.Value.Select(key => new ConfigKeyDto(kvp.Key, key)))
                .OrderBy(c => c.UnitId, StringComparer.Ordinal).ThenBy(c => c.Key, StringComparer.Ordinal)],
            [.. bundle.SecretReferences
                .SelectMany(kvp => kvp.Value.Select(reference => new SecretRefDto(kvp.Key, reference.Value)))
                .OrderBy(s => s.UnitId, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal)]);

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto, Options));
    }

    /// <summary>The manifest's content digest — what a promoter compares across environments.</summary>
    public static ArtifactDigest ComputeManifestDigest(ReleaseBundle bundle)
        => ArtifactDigest.Compute(Encode(bundle));

    /// <summary>
    /// Decodes a manifest. Returns false on anything malformed rather than throwing, so a registry can
    /// report a corrupt entry as a refusal with a reason instead of failing the caller's process.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> manifest, out ReleaseBundle? bundle)
    {
        bundle = null;

        ManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(manifest, Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (dto is null || dto.Artifacts is null || dto.Artifacts.Length == 0)
        {
            return false;
        }

        try
        {
            if (!BundleId.IsValid(dto.BundleId))
            {
                return false;
            }

            var artifacts = dto.Artifacts.Select(FromDto).ToArray();
            var contracts = (dto.SharedContracts ?? []).Select(FromDto).ToArray();

            var migrations = (dto.Migrations ?? []).Select(m => new UnitMigrationBinding(
                DeploymentUnitId.Parse(m.UnitId),
                new MigrationMetadata(m.Provider, m.MigrationIds ?? []))).ToArray();

            var configKeys = (dto.ConfigurationKeys ?? [])
                .GroupBy(c => c.UnitId, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)[.. g.Select(c => c.Key)],
                    StringComparer.Ordinal);

            var secrets = (dto.SecretReferences ?? [])
                .GroupBy(s => s.UnitId, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<SecretReference>)[.. g.Select(s => SecretReference.Parse(s.Name))],
                    StringComparer.Ordinal);

            bundle = new ReleaseBundle(
                dto.SchemaVersion,
                BundleId.Parse(dto.BundleId),
                DateTimeOffset.Parse(dto.CreatedAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                new ReleaseSource(dto.Source.CommitSha, dto.Source.RefName, dto.Source.Dirty),
                new BuilderIdentity(dto.Builder.Sdk, dto.Builder.Run, dto.Builder.Os, dto.Builder.Image),
                artifacts,
                contracts,
                migrations,
                configKeys,
                secrets);

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            return false;
        }
    }

    private static ArtifactDto ToDto(ReleaseArtifact artifact)
        => new(artifact.UnitId.Value, artifact.Digest.ToString(), artifact.SizeBytes);

    private static ReleaseArtifact FromDto(ArtifactDto dto)
        => new(DeploymentUnitId.Parse(dto.UnitId), ArtifactDigest.Parse(dto.Digest), dto.SizeBytes);

    private sealed record ManifestDto(
        [property: JsonPropertyOrder(0)] int SchemaVersion,
        [property: JsonPropertyOrder(1)] string BundleId,
        [property: JsonPropertyOrder(2)] string CreatedAt,
        [property: JsonPropertyOrder(3)] SourceDto Source,
        [property: JsonPropertyOrder(4)] BuilderDto Builder,
        [property: JsonPropertyOrder(5)] ArtifactDto[] Artifacts,
        [property: JsonPropertyOrder(6)] ArtifactDto[]? SharedContracts,
        [property: JsonPropertyOrder(7)] MigrationDto[]? Migrations,
        [property: JsonPropertyOrder(8)] ConfigKeyDto[]? ConfigurationKeys,
        [property: JsonPropertyOrder(9)] SecretRefDto[]? SecretReferences);

    private sealed record SourceDto(
        [property: JsonPropertyOrder(0)] string CommitSha,
        [property: JsonPropertyOrder(1)] string RefName,
        [property: JsonPropertyOrder(2)] bool Dirty);

    private sealed record BuilderDto(
        [property: JsonPropertyOrder(0)] string Sdk,
        [property: JsonPropertyOrder(1)] string Run,
        [property: JsonPropertyOrder(2)] string Os,
        [property: JsonPropertyOrder(3)] string? Image);

    private sealed record ArtifactDto(
        [property: JsonPropertyOrder(0)] string UnitId,
        [property: JsonPropertyOrder(1)] string Digest,
        [property: JsonPropertyOrder(2)] long SizeBytes);

    private sealed record MigrationDto(
        [property: JsonPropertyOrder(0)] string UnitId,
        [property: JsonPropertyOrder(1)] string Provider,
        [property: JsonPropertyOrder(2)] string SetDigest,
        [property: JsonPropertyOrder(3)] string[] MigrationIds);

    private sealed record ConfigKeyDto(
        [property: JsonPropertyOrder(0)] string UnitId,
        [property: JsonPropertyOrder(1)] string Key);

    private sealed record SecretRefDto(
        [property: JsonPropertyOrder(0)] string UnitId,
        [property: JsonPropertyOrder(1)] string Name);
}
