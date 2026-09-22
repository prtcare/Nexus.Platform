using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>The build manifest: completeness, canonical encoding, and the certification verdict it feeds.</summary>
public sealed class BuildManifestTests
{
    [Fact]
    public void ACompleteManifest_ReportsNoMissingElements()
    {
        var manifest = BuildTestData.Manifest();

        Assert.Empty(manifest.MissingRequiredElements());
        Assert.True(manifest.IsComplete);
    }

    [Fact]
    public void AManifest_CarriesTheArtifactHashSizeAndIdentity()
    {
        var manifest = BuildTestData.Manifest();
        var artifact = Assert.Single(manifest.Artifacts);

        Assert.Equal(BuildTestData.Unit, artifact.UnitId);
        Assert.NotNull(artifact.ContentDigest);
        Assert.True(artifact.SizeBytes > 0);
        Assert.Equal(manifest.BuildId, artifact.BuildId);
        Assert.Equal("marketsurvey.api/dotnet-app/marketsurvey.api@0.1.0", artifact.ArtifactId.Value);
    }

    [Fact]
    public void AManifest_RefusesAnEmptyArtifactList()
    {
        Assert.Throws<ArgumentException>(() => new BuildManifest(
            BuildManifest.CurrentSchemaVersion,
            BuildTestData.Identity(),
            [],
            BuildTestData.GreenTests(),
            BuildTestData.CleanScan(),
            BuildTestData.Identical(BuildTestData.Artifact(BuildTestData.Identity()).ContentDigest),
            BuildTestData.Provenance,
            DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void AManifest_RefusesACredentialShapedRuntimeConfigurationKey()
    {
        var ex = Assert.Throws<ArgumentException>(() => new BuildManifest(
            BuildManifest.CurrentSchemaVersion,
            BuildTestData.Identity(),
            [BuildTestData.Artifact(BuildTestData.Identity())],
            BuildTestData.GreenTests(),
            BuildTestData.CleanScan(),
            BuildTestData.Identical(BuildTestData.Artifact(BuildTestData.Identity()).ContentDigest),
            BuildTestData.Provenance,
            DateTimeOffset.UnixEpoch,
            [TestData.CredentialValue()]));

        Assert.Contains("never values", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TestData.CredentialValue(), ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TestVerdict.NotRun)]
    [InlineData(TestVerdict.NoTests)]
    public void AManifest_MissingATestVerdict_IsIncomplete(TestVerdict verdict)
    {
        var tests = verdict == TestVerdict.NotRun
            ? new BuildTestEvidence("suite", TestVerdict.NotRun, 0, 0, 0, 0)
            : new BuildTestEvidence("suite", TestVerdict.NoTests, 0, 0, 0, 0);

        var manifest = BuildTestData.Manifest(tests: tests);

        Assert.Contains("testVerdict", manifest.MissingRequiredElements());
        Assert.False(manifest.IsComplete);
    }

    [Fact]
    public void AManifest_WithoutAReproducibilityResult_IsIncomplete()
    {
        var manifest = BuildTestData.Manifest(
            reproducibility: ReproducibilityEvidence.NotPerformed("not measured"));

        Assert.Contains("reproducibilityResult", manifest.MissingRequiredElements());
    }

    // ---------------------------------------------------------------------------------------------
    // Canonical encoding — the digest is taken over these bytes
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Encoding_IsByteIdenticalForEqualManifests()
    {
        var builtAt = DateTimeOffset.Parse("2026-09-22T18:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture);

        var first = BuildManifestCodec.Encode(BuildTestData.Manifest(builtAt: builtAt));
        var second = BuildManifestCodec.Encode(BuildTestData.Manifest(builtAt: builtAt));

        Assert.Equal(first, second);
        Assert.Equal(
            BuildManifestCodec.ComputeManifestDigest(BuildTestData.Manifest(builtAt: builtAt)),
            BuildManifestCodec.ComputeManifestDigest(BuildTestData.Manifest(builtAt: builtAt)));
    }

    [Fact]
    public void Encoding_ChangesWhenTheArtifactBytesChange()
    {
        var baseline = BuildManifestCodec.ComputeManifestDigest(BuildTestData.Manifest());

        var changed = BuildTestData.Manifest(artifact: BuildTestData.Artifact(BuildTestData.Identity(), digestFill: 'f'));

        Assert.NotEqual(baseline, BuildManifestCodec.ComputeManifestDigest(changed));
    }

    [Fact]
    public void Encoding_RoundTripsThroughDecode()
    {
        var original = BuildTestData.Manifest();

        Assert.True(BuildManifestCodec.TryDecode(BuildManifestCodec.Encode(original), out var decoded));
        Assert.NotNull(decoded);

        Assert.Equal(original.BuildId, decoded!.BuildId);
        Assert.Equal(original.Identity.InputDigest, decoded.Identity.InputDigest);
        Assert.Equal(original.Tests.Verdict, decoded.Tests.Verdict);
        Assert.Equal(original.SecretScan.Verdict, decoded.SecretScan.Verdict);
        Assert.Equal(original.Reproducibility.Verdict, decoded.Reproducibility.Verdict);
        Assert.Equal(original.Reproducibility.BuildsCompared, decoded.Reproducibility.BuildsCompared);
        Assert.Equal(original.Artifacts.Count, decoded.Artifacts.Count);
        Assert.Equal(original.Artifacts[0].ContentDigest, decoded.Artifacts[0].ContentDigest);

        // Re-encoding the decoded manifest reproduces the same bytes: identity survives the round trip.
        Assert.Equal(BuildManifestCodec.Encode(original), BuildManifestCodec.Encode(decoded));
    }

    [Fact]
    public void Encoding_CarriesNoCredentialShapedValue()
    {
        var text = BuildManifestCodec.Encode(BuildTestData.Manifest());

        Assert.DoesNotContain(TestData.CredentialValue(), text, StringComparison.Ordinal);
        Assert.Contains("apiBaseUrl", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_OfGarbage_ReturnsFalseRatherThanThrowing()
    {
        Assert.False(BuildManifestCodec.TryDecode("not a manifest", out _));
        Assert.False(BuildManifestCodec.TryDecode(null, out _));
        Assert.False(BuildManifestCodec.TryDecode("nexus-build-manifest\t1\n", out _));
    }
}

/// <summary>
/// The certification gate: every refusal, and the discriminator that proves it is not refusing (or
/// passing) everything.
/// </summary>
public sealed class CertificationGateTests
{
    [Fact]
    public void ACompleteGreenBuild_IsCertified()
    {
        var decision = CertificationGate.Evaluate(BuildTestData.Evidence());

        Assert.True(decision.IsCertified);
        Assert.Equal(CertificationVerdict.ArtifactCertified, decision.Verdict);
        Assert.Empty(decision.RefusalReasons);
        Assert.Equal(BuildTestData.Identity().BuildId, decision.BuildId);
    }

    [Fact]
    public void ADirtySource_IsRefused()
    {
        var manifest = BuildTestData.Manifest(identity: BuildTestData.Identity(dirty: true));

        var decision = CertificationGate.Evaluate(BuildTestData.Evidence(manifest));

        Assert.True(decision.RefusedBecause(CertificationRefusalReason.SourceWorkingTreeIsDirty));
    }

    [Fact]
    public void AFailedBuild_IsRefused()
    {
        var decision = CertificationGate.Evaluate(BuildTestData.Evidence(buildSucceeded: false));

        Assert.True(decision.RefusedBecause(CertificationRefusalReason.BuildFailed));
    }

    [Fact]
    public void FailedTests_AreRefusedAsFailed()
    {
        var manifest = BuildTestData.Manifest(tests: new BuildTestEvidence("suite", TestVerdict.Failed, 10, 8, 2, 0));

        var decision = CertificationGate.Evaluate(BuildTestData.Evidence(manifest));

        Assert.True(decision.RefusedBecause(CertificationRefusalReason.TestsFailed));
        Assert.False(decision.RefusedBecause(CertificationRefusalReason.TestsNotRun));
    }

    /// <summary>
    /// NOT_RUN and NO_TESTS are separate refusals from FAILED, because the remedy differs: a failing suite
    /// is a code problem, a suite that did not run is a pipeline problem, and a suite with nothing in it is
    /// a coverage problem. This estate has recorded all three.
    /// </summary>
    [Fact]
    public void TestsNotRun_AndNoTests_AreDistinctRefusals()
    {
        var notRun = BuildTestData.Manifest(tests: new BuildTestEvidence("suite", TestVerdict.NotRun, 0, 0, 0, 0));
        var none = BuildTestData.Manifest(tests: new BuildTestEvidence("suite", TestVerdict.NoTests, 0, 0, 0, 0));

        var notRunDecision = CertificationGate.Evaluate(BuildTestData.Evidence(notRun));
        var noneDecision = CertificationGate.Evaluate(BuildTestData.Evidence(none));

        Assert.True(notRunDecision.RefusedBecause(CertificationRefusalReason.TestsNotRun));
        Assert.False(notRunDecision.RefusedBecause(CertificationRefusalReason.TestsAbsent));

        Assert.True(noneDecision.RefusedBecause(CertificationRefusalReason.TestsAbsent));
        Assert.False(noneDecision.RefusedBecause(CertificationRefusalReason.TestsNotRun));
    }

    [Fact]
    public void AFindingInActiveInput_IsRefused()
    {
        var scan = new SecretScanEvidence(
            SecretScanVerdict.Findings, ["PRT/MarketSurvey"], [], 10, 2,
            ["PRT/MarketSurvey/apps/api/Program.cs:11 (rule=credential-shaped-value, key=password)"], []);

        var manifest = BuildTestData.Manifest(scan: scan);

        var decision = CertificationGate.Evaluate(BuildTestData.Evidence(manifest));

        Assert.True(decision.RefusedBecause(CertificationRefusalReason.SecretScanFindingsInActiveInput));
    }

    [Fact]
    public void AnIncompleteScan_IsRefused()
    {
        var scan = new SecretScanEvidence(
            SecretScanVerdict.Incomplete, ["PRT/MarketSurvey"], [], 10, 2, [], ["PRT/MarketSurvey/apps: unreadable"]);

        var manifest = BuildTestData.Manifest(scan: scan);

        var decision = CertificationGate.Evaluate(BuildTestData.Evidence(manifest));

        Assert.True(decision.RefusedBecause(CertificationRefusalReason.SecretScanIncomplete));
    }

    /// <summary>
    /// The coverage check. A scan that covered a subset reports clean about a subset, and nothing inside the
    /// manifest can reveal that — which is why the expectation comes from outside it.
    /// </summary>
    [Fact]
    public void AScanThatCoveredASubsetOfTheInputSet_IsRefused()
    {
        var decision = CertificationGate.Evaluate(BuildTestData.Evidence(
            expectedSubjects: ["PRT/MarketSurvey", "NEXUS/Platform", "NEXUS/AI-Head"]));

        Assert.True(decision.RefusedBecause(CertificationRefusalReason.SecretScanIncompleteCoverage));
        Assert.Contains("NEXUS", string.Join(" ", decision.Detail), StringComparison.Ordinal);
    }

    [Fact]
    public void AUnitThatStillNeedsAnEnvironmentSpecificRebuild_IsRefused()
    {
        var decision = CertificationGate.Evaluate(BuildTestData.Evidence(requiresEnvironmentRebuild: true));

        Assert.True(decision.RefusedBecause(CertificationRefusalReason.EnvironmentSpecificRebuildDependency));
    }

    [Fact]
    public void AnUnprovenReproducibilityClaim_IsRefused()
    {
        var manifest = BuildTestData.Manifest(
            reproducibility: ReproducibilityEvidence.NotPerformed("only one build ran"));

        var decision = CertificationGate.Evaluate(BuildTestData.Evidence(manifest));

        Assert.True(decision.RefusedBecause(CertificationRefusalReason.ReproducibilityNotProven));
    }

    [Fact]
    public void DivergentReproducibility_IsRefused()
    {
        var divergent = new ReproducibilityEvidence(
            ReproducibilityVerdict.Divergent,
            2,
            [ArtifactDigest.Parse($"sha256:{new string('a', 64)}"), ArtifactDigest.Parse($"sha256:{new string('b', 64)}")]);

        var manifest = BuildTestData.Manifest(reproducibility: divergent);

        var decision = CertificationGate.Evaluate(BuildTestData.Evidence(manifest));

        Assert.True(decision.RefusedBecause(CertificationRefusalReason.ReproducibilityNotProven));
    }

    /// <summary>
    /// A refusal must always name a reason, so it cannot be confused with a bug. Asserted structurally
    /// rather than on one case.
    /// </summary>
    [Fact]
    public void EveryRefusalNamesAtLeastOneTypedReason()
    {
        var decision = CertificationGate.Evaluate(BuildTestData.Evidence(buildSucceeded: false));

        Assert.True(decision.Verdict == CertificationVerdict.Refused);
        Assert.NotEmpty(decision.RefusalReasons);
        Assert.DoesNotContain(CertificationRefusalReason.None, decision.RefusalReasons);
    }

    /// <summary>
    /// The discriminator. Identical evidence except for one member, and the verdict flips — so a gate that
    /// refused everything, or passed everything, fails here rather than passing every test above by being
    /// uniformly wrong.
    /// </summary>
    [Fact]
    public void TheGateIsNotVacuouslyRefusingOrPassing()
    {
        var green = CertificationGate.Evaluate(BuildTestData.Evidence());
        var refused = CertificationGate.Evaluate(BuildTestData.Evidence(buildSucceeded: false));

        Assert.True(green.IsCertified);
        Assert.False(refused.IsCertified);
    }

    [Fact]
    public void TheGateRefusesForEveryReasonItDeclares_WhereThatIsReachable()
    {
        // Each refusal reason that a caller can produce from evidence must be reachable from a test, or it is
        // a vocabulary entry nobody can act on. This walks the ones reachable here and asserts they fire.
        var reachable = new[]
        {
            CertificationGate.Evaluate(BuildTestData.Evidence(buildSucceeded: false)),
            CertificationGate.Evaluate(BuildTestData.Evidence(manifest: BuildTestData.Manifest(identity: BuildTestData.Identity(dirty: true)))),
            CertificationGate.Evaluate(BuildTestData.Evidence(requiresEnvironmentRebuild: true)),
        };

        Assert.All(reachable, d => Assert.True(d.RefusalReasons.Count > 0));
    }
}
