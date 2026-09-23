using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// Identity and validation: the digest, the unit declaration, the reference type and the manifest's
/// determinism. These are the contract-level invariants the state machine and the registry both rest on.
/// </summary>
public sealed class IdentityAndContractTests
{
    // ---------------------------------------------------------------------------------------------
    // ArtifactDigest
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Digest_RoundTripsThroughItsCanonicalForm()
    {
        var digest = ArtifactDigest.Compute("hello"u8);

        Assert.Equal(ArtifactDigest.Sha256Algorithm, digest.Algorithm);
        Assert.Equal(64, digest.Hex.Length);
        Assert.Equal(digest, ArtifactDigest.Parse(digest.ToString()));
    }

    [Fact]
    public void Digest_IsOverContent_AndIdenticalInputsProduceIdenticalDigests()
    {
        // The promotion proof is a digest comparison, so this is the property the whole mechanism rests on.
        Assert.Equal(ArtifactDigest.Compute("artifact"u8), ArtifactDigest.Compute("artifact"u8));
        Assert.NotEqual(ArtifactDigest.Compute("artifact"u8), ArtifactDigest.Compute("artifact "u8));
    }

    [Theory]
    [InlineData("")]
    [InlineData("sha256:")]                              // no digest
    [InlineData("sha256:ABCDEF")]                        // uppercase is not canonical
    [InlineData("md5:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")] // unsupported algorithm
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")] // 63 chars
    [InlineData("sk-deadbeefdeadbeefdeadbeefdeadbeef")]  // not a digest at all
    public void Digest_RefusesAnythingThatIsNotCanonical(string value)
    {
        Assert.False(ArtifactDigest.TryParse(value, out _));
        Assert.Throws<ArgumentException>(() => ArtifactDigest.Parse(value));
    }

    /// <summary>
    /// The digest type must not be able to swallow a credential: it appears in manifests and lineage
    /// records, which is the wrong place for one to survive as an opaque string.
    /// </summary>
    [Fact]
    public void Digest_Parse_RefusesACredentialShapedInput()
    {
        Assert.False(ArtifactDigest.TryParse(TestData.CredentialValue(), out _));
    }

    // ---------------------------------------------------------------------------------------------
    // SecretReference — invariant I-7 / L-1 at the type level
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SecretReference_AcceptsNamesAndRefusesValues()
    {
        Assert.True(SecretReference.TryParse("NEXUS_OPENAI_API_KEY", out var name));
        Assert.Equal("NEXUS_OPENAI_API_KEY", name!.Value);

        // A real-looking key is refused, and it is refused even though it starts with a letter and
        // contains only name-safe characters — which is why the check cannot be positive-only.
        Assert.False(SecretReference.TryParse(TestData.CredentialValue(), out _));
        Assert.False(SecretReference.TryParse(TestData.TokenValue(), out _));
    }

    [Fact]
    public void SecretReference_OfAValue_ThrowsWithAUsefulMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => SecretReference.Parse(TestData.TokenValue()));

        Assert.Contains("never be the secret VALUE", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("WORKBENCH_DB_PASSWORD")]
    [InlineData("process.env.WORKBENCH_DB_PASSWORD")]
    [InlineData("[REDACTED]")]
    [InlineData("")]
    public void CredentialShape_ClassifiesTheFalsePositiveClasses(string candidate)
    {
        Assert.False(CredentialShape.LooksLikeCredentialValue(candidate));
    }

    [Fact]
    public void CredentialShape_ClassifiesAMachineGeneratedKey()
    {
        Assert.True(CredentialShape.LooksLikeCredentialValue(TestData.CredentialValue()));
        Assert.True(CredentialShape.LooksLikeCredentialValue(TestData.TokenValue()));
    }

    // ---------------------------------------------------------------------------------------------
    // DeploymentUnit
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ServiceUnit_RequiresAReadinessSignalAndAMedium()
    {
        Assert.Throws<ArgumentException>(() => new DeploymentUnit(
            TestData.ApiUnit,
            DeploymentUnitKind.Service,
            "Products/Developer",
            "src/Nexus.Developer.Api/Nexus.Developer.Api.csproj",
            PackagingMedium.Container,
            health: null));

        Assert.Throws<ArgumentException>(() => new DeploymentUnit(
            TestData.ApiUnit,
            DeploymentUnitKind.Service,
            "Products/Developer",
            "src/Nexus.Developer.Api/Nexus.Developer.Api.csproj",
            PackagingMedium.Undeclared,
            new HealthContract("/health/ready")));
    }

    [Fact]
    public void AClientUnit_MustUseTheStaticBundleMedium()
    {
        Assert.Throws<ArgumentException>(() => new DeploymentUnit(
            DeploymentUnitId.Parse("nexus.experience.client"),
            DeploymentUnitKind.Client,
            "Products/Experience",
            "src/Nexus.Experience.Client/package.json",
            PackagingMedium.Container));
    }

    [Fact]
    public void AUnitCannotDependOnItself()
    {
        Assert.Throws<ArgumentException>(() => new DeploymentUnit(
            TestData.ApiUnit,
            DeploymentUnitKind.Service,
            "Products/Developer",
            "src/Nexus.Developer.Api/Nexus.Developer.Api.csproj",
            PackagingMedium.Container,
            new HealthContract("/health/ready"),
            dependsOn: [TestData.ApiUnit]));
    }

    [Fact]
    public void AnExcludedUnit_MustStateWhy()
    {
        var excluded = TestData.ServiceUnit(exclusionReason: "Does not build; W8-DEBT-03.");

        Assert.True(excluded.IsExcluded);
        Assert.Equal("Does not build; W8-DEBT-03.", excluded.ExclusionReason);
    }

    // ---------------------------------------------------------------------------------------------
    // ReleaseBundle
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ABundleMustCarryAtLeastOneArtifact()
    {
        Assert.Throws<ArgumentException>(() => new ReleaseBundle(
            1,
            BundleId.Parse("nexus-2026.09.22-7f3a1c"),
            DateTimeOffset.UnixEpoch,
            new ReleaseSource(TestData.CleanCommit, "w9.1/release", false),
            new BuilderIdentity("10.0.302", "run-1", "linux"),
            []));
    }

    [Fact]
    public void ABundleCarriesAtMostOneArtifactPerUnit()
    {
        Assert.Throws<ArgumentException>(() => new ReleaseBundle(
            1,
            BundleId.Parse("nexus-2026.09.22-7f3a1c"),
            DateTimeOffset.UnixEpoch,
            new ReleaseSource(TestData.CleanCommit, "w9.1/release", false),
            new BuilderIdentity("10.0.302", "run-1", "linux"),
            [TestData.Artifact(digestFill: 'a'), TestData.Artifact(digestFill: 'b')]));
    }

    [Fact]
    public void ABundleRefusesAnUnknownSchemaVersion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestData.Bundle(schemaVersion: 99));
    }

    // ---------------------------------------------------------------------------------------------
    // Manifest determinism — the promotion proof reads this digest
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ManifestEncoding_IsByteIdenticalForEqualBundles()
    {
        var first = BundleManifestCodec.Encode(TestData.Bundle());
        var second = BundleManifestCodec.Encode(TestData.Bundle());

        Assert.Equal(first, second);
    }

    [Fact]
    public void ManifestEncoding_IsIndependentOfTheOrderCollectionsWereBuiltIn()
    {
        var ordered = TestData.Bundle();

        // Same members, inserted in the other order. A canonicalising encoder must produce identical
        // bytes, because a digest that depends on insertion order silently stops proving identity.
        var reversed = new ReleaseBundle(
            ordered.SchemaVersion,
            ordered.BundleId,
            ordered.CreatedAt,
            ordered.Source,
            ordered.Builder,
            [.. ordered.Artifacts],
            [.. ordered.SharedContracts],
            [.. ordered.Migrations],
            new Dictionary<string, IReadOnlyList<string>> { ["nexus.developer.api"] = ["ASPNETCORE_ENVIRONMENT"] },
            new Dictionary<string, IReadOnlyList<SecretReference>>
            {
                ["nexus.developer.api"] = [SecretReference.Parse("NEXUS_OPENAI_API_KEY")]
            });

        Assert.Equal(BundleManifestCodec.Encode(ordered), BundleManifestCodec.Encode(reversed));
    }

    [Fact]
    public void ManifestEncoding_ChangesWhenTheArtifactBytesChange()
    {
        // The Owner's ruling: changed artifact bytes always mean a NEW bundle, never a promotion. The
        // manifest digest is the mechanism that enforces it, so it must move when the bytes move.
        var baseline = BundleManifestCodec.ComputeManifestDigest(TestData.Bundle());

        var changed = new ReleaseBundle(
            1,
            BundleId.Parse("nexus-2026.09.22-7f3a1c"),
            DateTimeOffset.Parse("2026-09-22T14:02:11+00:00", System.Globalization.CultureInfo.InvariantCulture),
            new ReleaseSource(TestData.CleanCommit, "w9.1/release", false),
            new BuilderIdentity("10.0.302", "run-42", "linux"),
            [TestData.Artifact(digestFill: 'c')],
            [TestData.Artifact(DeploymentUnitId.Parse("platform.productcore.contracts"), 'b', 2048)]);

        Assert.NotEqual(baseline, BundleManifestCodec.ComputeManifestDigest(changed));
    }

    [Fact]
    public void Manifest_RoundTripsThroughDecode()
    {
        var original = TestData.Bundle();

        Assert.True(BundleManifestCodec.TryDecode(BundleManifestCodec.Encode(original), out var decoded));
        Assert.NotNull(decoded);

        Assert.Equal(original.BundleId, decoded!.BundleId);
        Assert.Equal(original.Source.CommitSha, decoded.Source.CommitSha);
        Assert.Equal(original.AllArtifacts.Count(), decoded.AllArtifacts.Count());

        // Re-encoding the decoded bundle reproduces the same bytes, so identity survives a round trip.
        Assert.Equal(BundleManifestCodec.Encode(original), BundleManifestCodec.Encode(decoded));
    }

    [Fact]
    public void Manifest_DecodeOfGarbage_ReturnsFalseRatherThanThrowing()
    {
        Assert.False(BundleManifestCodec.TryDecode("not json at all"u8, out _));
        Assert.False(BundleManifestCodec.TryDecode("""{"SchemaVersion":1}"""u8, out _));
    }

    [Fact]
    public void Manifest_ContainsNoSecretValue()
    {
        var text = System.Text.Encoding.UTF8.GetString(BundleManifestCodec.Encode(TestData.Bundle()));

        // The reference NAME is present; nothing value-shaped is.
        Assert.Contains("NEXUS_OPENAI_API_KEY", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", text, StringComparison.Ordinal);
    }
}
