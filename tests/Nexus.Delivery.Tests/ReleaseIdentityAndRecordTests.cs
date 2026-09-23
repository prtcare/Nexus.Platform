using System.Reflection;
using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// The release identity: that it is derived rather than assigned, that it cannot carry an environment, and
/// that the release record is immutable and canonically encodable.
/// </summary>
public sealed class ReleaseIdentityTests
{
    private static readonly string[] EnvironmentVocabulary =
        ["environment", "env", "dev", "test", "prod", "production", "staging", "hostname", "endpoint", "region", "cluster", "instance"];

    [Fact]
    public void ReleaseId_IsDerivedFromTheIdentity_AndIsStable()
    {
        var first = ReleaseTestData.Identity();
        var second = ReleaseTestData.Identity();

        Assert.Equal(first.ReleaseId, second.ReleaseId);
        Assert.StartsWith("rel-", first.ReleaseId.Value, StringComparison.Ordinal);
        Assert.Equal(64, first.InputDigest.Hex.Length);
        Assert.Equal("rel-" + first.InputDigest.Hex[..16], first.ReleaseId.Value);
    }

    [Fact]
    public void ReleaseId_ChangesWhenAnyIdentityMemberChanges()
    {
        var baseline = ReleaseTestData.Identity().ReleaseId;

        Assert.NotEqual(baseline, ReleaseTestData.Identity(commit: BuildTestData.CommitB).ReleaseId);
        Assert.NotEqual(baseline, ReleaseTestData.Identity(version: "0.2.0").ReleaseId);
        Assert.NotEqual(baseline, ReleaseTestData.Identity(digestFill: 'b').ReleaseId);
        Assert.NotEqual(baseline, ReleaseTestData.Identity(refName: "refs/tags/release/marketsurvey.api/9.9.9").ReleaseId);
    }

    /// <summary>
    /// TASK 1's requirement, asserted structurally rather than trusted: the release identity has no member
    /// whose name is environment-shaped. A future member added in good faith is exactly how this property
    /// would be lost, so a reflection test is the control and the prose is not.
    /// </summary>
    [Fact]
    public void ReleaseIdentity_HasNoEnvironmentShapedMember()
    {
        var names = typeof(ReleaseIdentity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        var leaked = names
            .Where(n => EnvironmentVocabulary.Any(v => n.Contains(v, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            leaked.Count == 0,
            "The release identity names an environment, so the same ReleaseId could not be promoted through environments: "
            + string.Join(", ", leaked));
    }

    [Fact]
    public void ReleaseRecord_HasNoEnvironmentShapedMember()
    {
        var names = typeof(ReleaseRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        var leaked = names
            .Where(n => EnvironmentVocabulary.Any(v => n.Contains(v, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(leaked.Count == 0, "The release record names an environment: " + string.Join(", ", leaked));
    }

    /// <summary>
    /// Environment is deployment state, not release identity. The promotion machine carries it; the release
    /// must not, and this asserts the release carries no member of that type at all.
    /// </summary>
    [Fact]
    public void ReleaseIdentity_CarriesNoDeploymentEnvironment()
    {
        var types = typeof(ReleaseIdentity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .Concat(typeof(ReleaseIdentity)
                .GetConstructors()
                .SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
            .ToList();

        Assert.DoesNotContain(typeof(DeploymentEnvironmentId), types);
        Assert.DoesNotContain(typeof(PromotionState), types);
        Assert.DoesNotContain(typeof(DeploymentEnvironment), types);
    }

    [Fact]
    public void ReleaseIdentity_RefusesAVersionThatDisagreesWithTheArtifact()
    {
        var artifact = ReleaseTestData.ArtifactIdentity(version: "0.1.0");

        var error = Assert.Throws<ArgumentException>(() => new ReleaseIdentity(
            BuildTestData.Unit,
            "0.2.0",
            BuildTestData.Identity().BuildId,
            [BuildTestData.CommitA],
            "refs/tags/release/marketsurvey.api/0.2.0",
            [artifact]));

        Assert.Contains("derived from the certified artifact", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseIdentity_RefusesMoreThanOneUnit()
    {
        var error = Assert.Throws<ArgumentException>(() => new ReleaseIdentity(
            BuildTestData.Unit,
            "0.1.0",
            BuildTestData.Identity().BuildId,
            [BuildTestData.CommitA],
            "refs/tags/release/marketsurvey.api/0.1.0",
            [ReleaseTestData.ArtifactIdentity(), ReleaseTestData.ArtifactIdentity(DeploymentUnitId.Parse("nexus.atlasgraph.api"))]));

        Assert.Contains("one deployment unit", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Iteration order must not leak into the identity: the same commits presented in a different order, or
    /// with a duplicate, describe the same release and must therefore derive the same id.
    /// </summary>
    [Fact]
    public void SourceCommits_AreOrderedAndDeDuplicated_SoIterationOrderCannotLeakIn()
    {
        ReleaseIdentity Build(params string[] commits) => new(
            BuildTestData.Unit,
            "0.1.0",
            BuildTestData.Identity().BuildId,
            commits,
            "refs/tags/release/marketsurvey.api/0.1.0",
            [ReleaseTestData.ArtifactIdentity()]);

        var ordered = Build(BuildTestData.CommitA, BuildTestData.CommitB);
        var shuffled = Build(BuildTestData.CommitB, BuildTestData.CommitA);
        var duplicated = Build(BuildTestData.CommitB, BuildTestData.CommitA, BuildTestData.CommitB);

        Assert.Equal([BuildTestData.CommitA, BuildTestData.CommitB], ordered.SourceCommits);
        Assert.Equal(ordered.ReleaseId, shuffled.ReleaseId);
        Assert.Equal(ordered.ReleaseId, duplicated.ReleaseId);
    }
}

/// <summary>The release bundle's canonical encoding and the immutability key derived from it.</summary>
public sealed class ReleaseRecordCodecTests
{
    [Fact]
    public void Encode_IsByteIdenticalForEqualReleases()
    {
        Assert.Equal(
            ReleaseRecordCodec.Encode(ReleaseTestData.Record()),
            ReleaseRecordCodec.Encode(ReleaseTestData.Record()));
    }

    [Fact]
    public void RecordDigest_IgnoresWhenTheRecordWasWritten()
    {
        var first = ReleaseTestData.Record(createdAt: DateTimeOffset.Parse("2026-09-23T10:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture));
        var second = ReleaseTestData.Record(createdAt: DateTimeOffset.Parse("2027-01-01T00:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture));

        // CreatedAt is when a record was written, not what it says. Including it would make the same
        // release reassembled on two days look like two releases.
        Assert.Equal(first.ComputeRecordDigest(), second.ComputeRecordDigest());
        Assert.Equal(first.ReleaseId, second.ReleaseId);
    }

    [Fact]
    public void RecordDigest_ChangesWhenNonIdentityMetadataChanges()
    {
        var baseline = ReleaseTestData.Record();

        Assert.NotEqual(
            baseline.ComputeRecordDigest(),
            ReleaseTestData.Record(configurationSchemaVersion: "runtime-config-v2").ComputeRecordDigest());

        Assert.NotEqual(
            baseline.ComputeRecordDigest(),
            ReleaseTestData.Record(rollback: RollbackMetadata.NoPreviousAcceptedRelease(false, "A different basis.")).ComputeRecordDigest());

        // Both still describe the same release identity, which is exactly why the registry must compare the
        // record digest and not the release id alone.
        Assert.Equal(baseline.ReleaseId, ReleaseTestData.Record(configurationSchemaVersion: "runtime-config-v2").ReleaseId);
    }

    [Fact]
    public void TryDecode_RoundTripsEveryMember()
    {
        var release = ReleaseTestData.Record(
            rollback: RollbackMetadata.ToPreviousRelease(ReleaseTestData.Identity(digestFill: 'c').ReleaseId, true, true, "prior release accepted in ENV-DEV"),
            contracts: [new ContractVersion("Nexus.ProductCore.Contracts", "0.1.0"), new ContractVersion("Nexus.Delivery.Contracts", "0.2.0")],
            secretReferences: [SecretReference.Parse("NEXUS_PROVIDER_CREDENTIAL_REF")],
            configurationKeys: ["apiBaseUrl", "NEXUS_RUNTIME_CONFIG_PATH"]);

        var encoded = ReleaseRecordCodec.Encode(release);

        Assert.True(ReleaseRecordCodec.TryDecode(encoded, out var decoded));
        Assert.NotNull(decoded);

        Assert.Equal(release.ReleaseId, decoded!.ReleaseId);
        Assert.Equal(release.ComputeRecordDigest(), decoded.ComputeRecordDigest());
        Assert.Equal(ReleaseRecordCodec.Encode(release), ReleaseRecordCodec.Encode(decoded));
        Assert.Equal(RollbackReferenceState.PreviousAcceptedReleaseExists, decoded.Rollback.State);
        Assert.Equal(release.Rollback.PreviousReleaseId, decoded.Rollback.PreviousReleaseId);
        Assert.Equal(2, decoded.ContractVersions.Count);
        Assert.Single(decoded.SecretReferences);
        Assert.Equal(["NEXUS_RUNTIME_CONFIG_PATH", "apiBaseUrl"], decoded.ConfigurationKeys);
    }

    [Fact]
    public void TryDecode_RoundTripsAMigrationAssessmentThatRequiresOne()
    {
        var release = ReleaseTestData.Record(migrations: MigrationAssessment.Required(
            new MigrationMetadata("ef-sqlserver", ["20260827064146_InitialSqlSchema", "20260901090000_SpatialAndAudit"]),
            "0.0.0",
            "0.1.0",
            MigrationCompatibility.Behind,
            backupRequired: true,
            MigrationReversibility.ForwardFixOnly,
            "Read the migration directory inside the published unit."));

        Assert.True(ReleaseRecordCodec.TryDecode(ReleaseRecordCodec.Encode(release), out var decoded));

        Assert.Equal(MigrationRequirementState.MigrationsRequired, decoded!.Migrations.State);
        Assert.Equal(2, decoded.Migrations.Metadata.MigrationIds.Count);
        Assert.True(decoded.Migrations.BackupRequired);
        Assert.Equal(MigrationReversibility.ForwardFixOnly, decoded.Migrations.Reversibility);
        Assert.Equal(release.ComputeRecordDigest(), decoded.ComputeRecordDigest());
    }

    [Fact]
    public void TryDecode_RefusesARecordWhoseReleaseIdDisagreesWithItsIdentity()
    {
        var encoded = ReleaseRecordCodec.Encode(ReleaseTestData.Record());
        var text = System.Text.Encoding.UTF8.GetString(encoded);
        var original = ReleaseTestData.Record().ReleaseId.Value;
        var forged = ReleaseId.Parse("rel-" + new string('0', 16)).Value;

        var tampered = System.Text.Encoding.UTF8.GetBytes(text.Replace(original, forged, StringComparison.Ordinal));

        // The release id is re-derived on decode rather than trusted, so a file whose id disagrees with the
        // identity beside it cannot be read as the release it claims to be.
        Assert.False(ReleaseRecordCodec.TryDecode(tampered, out _));
    }

    [Fact]
    public void TryDecode_ReturnsFalseForGarbage_RatherThanThrowing()
    {
        Assert.False(ReleaseRecordCodec.TryDecode("not json"u8, out _));
        Assert.False(ReleaseRecordCodec.TryDecode("{}"u8, out _));
        Assert.False(ReleaseRecordCodec.TryDecode(new byte[0], out _));
    }

    [Fact]
    public void Record_IsImmutable_EveryCollectionIsAReadOnlyCopy()
    {
        var keys = new List<string> { "api" };
        var release = ReleaseTestData.Record(configurationKeys: keys);

        keys.Add("mutated-after-construction");

        Assert.Single(release.ConfigurationKeys);

        var properties = typeof(ReleaseRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.All(properties, p => Assert.Null(p.SetMethod));
    }
}
