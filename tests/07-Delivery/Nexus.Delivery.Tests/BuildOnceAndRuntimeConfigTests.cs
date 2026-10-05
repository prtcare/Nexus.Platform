using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// The build-once rules. Each of the three Task 3 properties has a refusal, and each has a negative control
/// beside it so a rule that refused everything would fail rather than pass.
/// </summary>
public sealed class BuildOnceGuardTests
{
    private static PromotionAttempt Attempt(
        ArtifactDigest? certified = null,
        ArtifactDigest? presented = null,
        bool certifiedFlag = true,
        bool requestsRebuild = false,
        bool includeCertifiedDigest = true)
    {
        var identity = BuildTestData.Identity();
        var digest = certified ?? ArtifactDigest.Parse($"sha256:{new string('a', 64)}");

        return new PromotionAttempt(
            BundleId.Parse("nexus-2026.09.22-7f3a1c"),
            ArtifactId.For(identity.UnitId, ArtifactType.DotnetApplication, "marketsurvey.api", "0.1.0"),
            identity.BuildId,
            includeCertifiedDigest ? digest : null,
            presented,
            DeploymentEnvironmentId.TestEnv,
            requestsRebuild,
            certifiedFlag);
    }

    [Fact]
    public void ACertifiedArtifactReferencedByDigest_MayBePromoted()
    {
        var verdict = BuildOnceGuard.Evaluate(BuildTestData.Identity(), Attempt());

        Assert.True(verdict.IsPermitted);
        Assert.Empty(verdict.RefusalReasons);
    }

    [Fact]
    public void APromotionThatAsksForARebuild_IsRefused()
    {
        var verdict = BuildOnceGuard.Evaluate(BuildTestData.Identity(), Attempt(requestsRebuild: true));

        Assert.True(verdict.RefusedBecause(BuildOnceRefusalReason.EnvironmentSpecificRebuildRequested));
    }

    [Fact]
    public void AnUncertifiedArtifact_IsRefused()
    {
        var verdict = BuildOnceGuard.Evaluate(BuildTestData.Identity(), Attempt(certifiedFlag: false));

        Assert.True(verdict.RefusedBecause(BuildOnceRefusalReason.ArtifactNotCertified));
    }

    /// <summary>
    /// The rule that distinguishes a promotion from a build: a promotion references bytes someone else
    /// produced. An attempt that brings its own — no certified digest — is refused.
    /// </summary>
    [Fact]
    public void AnAttemptThatSuppliesItsOwnBytes_IsRefused()
    {
        var verdict = BuildOnceGuard.Evaluate(BuildTestData.Identity(), Attempt(includeCertifiedDigest: false));

        Assert.True(verdict.RefusedBecause(BuildOnceRefusalReason.ArtifactSuppliedRatherThanReferenced));
    }

    /// <summary>
    /// The rule the Owner's environment ruling turns on: changed bytes are a NEW build, never a promotion
    /// that quietly replaces what a release id already holds.
    /// </summary>
    [Fact]
    public void ChangedBytesForAnExistingReleaseId_AreRefused()
    {
        var presented = ArtifactDigest.Parse($"sha256:{new string('f', 64)}");

        var verdict = BuildOnceGuard.Evaluate(BuildTestData.Identity(), Attempt(presented: presented));

        Assert.True(verdict.RefusedBecause(BuildOnceRefusalReason.DigestChangedForExistingReleaseId));
        Assert.Contains("NEW build", string.Join(" ", verdict.Detail), StringComparison.Ordinal);
    }

    [Fact]
    public void IdenticalBytesPresentedForAnExistingReleaseId_ArePermitted()
    {
        // The negative control for the rule above: presenting the *same* digest is not a substitution.
        var digest = ArtifactDigest.Parse($"sha256:{new string('a', 64)}");

        var verdict = BuildOnceGuard.Evaluate(BuildTestData.Identity(), Attempt(certified: digest, presented: digest));

        Assert.True(verdict.IsPermitted);
    }

    /// <summary>
    /// The environment-in-identity check, with its control. The suffix is caught because nothing prevents
    /// an environment being smuggled through a free-text field — the type has no environment member, but it
    /// does have string members, and a pipeline writing <c>-c Release-ENV-PROD</c> would otherwise produce
    /// two builds that differ only by environment and look perfectly valid.
    /// </summary>
    [Fact]
    public void AnEnvironmentSmuggledIntoTheBuildConfiguration_IsCaught()
    {
        var clean = BuildTestData.Identity(configuration: "Release");
        var smuggled = BuildTestData.Identity(configuration: "Release-ENV-PROD");

        Assert.Empty(BuildOnceGuard.InspectIdentity(clean));

        var findings = BuildOnceGuard.InspectIdentity(smuggled);

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal(BuildOnceRefusalReason.EnvironmentInBuildIdentity, f.Reason));
    }

    [Fact]
    public void AnEnvironmentSmuggledIntoTheContainerImage_IsCaught()
    {
        var identity = BuildTestData.Identity(
            toolchain: new ToolchainIdentity("10.0.302", "10.0.0", "linux-x64", "builder:ENV-TEST"));

        Assert.NotEmpty(BuildOnceGuard.InspectIdentity(identity));
    }

    [Fact]
    public void AnEnvironmentSmuggledIntoAReason_IsCaught()
    {
        // The dependency-lock note is free text and is recorded in the manifest, so it is a real carrier.
        var identity = BuildTestData.Identity(
            lockState: DependencyLockState.Unlocked("Unlocked for ENV-PROD because the feed differs."));

        var findings = BuildOnceGuard.InspectIdentity(identity);

        Assert.NotEmpty(findings);
    }

    [Fact]
    public void TheGuardIsNotVacuouslyPermittingOrRefusing()
    {
        var permitted = BuildOnceGuard.Evaluate(BuildTestData.Identity(), Attempt());
        var refused = BuildOnceGuard.Evaluate(BuildTestData.Identity(), Attempt(requestsRebuild: true));

        Assert.True(permitted.IsPermitted);
        Assert.False(refused.IsPermitted);
    }

    [Fact]
    public void ARefusalAlwaysNamesAtLeastOneReason()
    {
        var verdict = BuildOnceGuard.Evaluate(BuildTestData.Identity(), Attempt(certifiedFlag: false, requestsRebuild: true));

        Assert.False(verdict.IsPermitted);
        Assert.NotEmpty(verdict.RefusalReasons);
        Assert.DoesNotContain(BuildOnceRefusalReason.None, verdict.RefusalReasons);
    }
}

/// <summary>
/// The client runtime configuration: the pattern that removes the per-environment rebuild, and the security
/// rule that makes it safe. A client bundle is public, so what moves out of the build must be safe to
/// publish — and a credential-shaped value is refused rather than warned about.
/// </summary>
public sealed class ClientRuntimeConfigurationTests
{
    private static ClientRuntimeConfigurationValidator Validator(bool production = false, params string[] required)
        => new(required.Length > 0 ? required : ["api"], production);

    private static string Json(string endpoint, int schemaVersion = ClientRuntimeConfiguration.CurrentSchemaVersion, string environment = "ENV-DEV")
        => $$"""
             { "schemaVersion": {{schemaVersion}}, "environment": "{{environment}}", "endpoints": { "api": "{{endpoint}}" } }
             """;

    [Fact]
    public void AWellFormedConfiguration_IsValid()
    {
        var validation = Validator().Parse(Json("https://api.test.example"));

        Assert.True(validation.IsValid);
        Assert.NotNull(validation.Configuration);
        Assert.Equal("https://api.test.example", validation.Configuration!.Endpoints["api"]);
    }

    [Fact]
    public void AMissingConfiguration_IsTypedAsMissing_NotAnException()
    {
        var validation = Validator().Parse(null);

        Assert.False(validation.IsValid);
        Assert.True(validation.RefusedBecause(RuntimeConfigurationRefusalReason.ConfigurationMissing));
    }

    [Fact]
    public void AMalformedConfiguration_IsTypedAsMalformed()
    {
        var validation = Validator().Parse("{ not json");

        Assert.True(validation.RefusedBecause(RuntimeConfigurationRefusalReason.ConfigurationMalformed));
    }

    [Fact]
    public void AnUnsupportedSchemaVersion_IsRefused()
    {
        var validation = Validator().Parse(Json("https://api.test.example", schemaVersion: 99));

        Assert.True(validation.RefusedBecause(RuntimeConfigurationRefusalReason.SchemaVersionUnsupported));
    }

    [Fact]
    public void AMissingRequiredEndpoint_IsRefused()
    {
        var validation = Validator(required: ["api", "telemetry"]).Parse(Json("https://api.test.example"));

        Assert.True(validation.RefusedBecause(RuntimeConfigurationRefusalReason.RequiredKeyMissing));
    }

    [Fact]
    public void AnUnratifiedEnvironmentLabel_IsRefused()
    {
        var validation = Validator().Parse(Json("https://api.test.example", environment: "staging"));

        Assert.True(validation.RefusedBecause(RuntimeConfigurationRefusalReason.EnvironmentNotRatified));
    }

    /// <summary>
    /// The security half of the pattern. Configuration moved out of the build is fetched by the same
    /// browser that reads the bundle, so it inherits the bundle's publicness — a credential here is not a
    /// bad practice, it is a credential being published to every user.
    /// </summary>
    [Fact]
    public void ACredentialShapedValue_IsRefused_AndItsValueIsNotReproduced()
    {
        var secret = TestData.CredentialValue();

        var validation = Validator().Parse(Json(secret));

        Assert.True(validation.RefusedBecause(RuntimeConfigurationRefusalReason.SecretValuePresent));
        Assert.DoesNotContain(secret, string.Join("\n", validation.Detail), StringComparison.Ordinal);
    }

    [Fact]
    public void AUrlCarryingUserInfo_IsRefusedAsACredential()
    {
        var validation = Validator().Parse(Json("https://survey:hunter2isnotreal@api.test.example"));

        Assert.True(validation.RefusedBecause(RuntimeConfigurationRefusalReason.SecretValuePresent));
    }

    [Fact]
    public void PlainHttpInProduction_IsRefused_ButIsFineInTest()
    {
        var inTest = Validator().Parse(Json("http://127.0.0.1:5080"));
        var inProduction = Validator(production: true).Parse(Json("http://127.0.0.1:5080", environment: "ENV-PROD"));

        Assert.True(inTest.IsValid);
        Assert.True(inProduction.RefusedBecause(RuntimeConfigurationRefusalReason.InsecureEndpointInProduction));
    }

    /// <summary>
    /// The typed startup failure. A missing configuration must fail loudly at startup — not render a page
    /// that quietly talks to whatever the artifact was built with, which is the failure this replaces: the
    /// shipped MarketSurvey bundle carries a loopback fallback endpoint.
    /// </summary>
    [Fact]
    public async Task LoadOrFail_ThrowsATypedFailure_WhenNothingIsAvailable()
    {
        var validator = Validator();

        var exception = await Assert.ThrowsAsync<RuntimeConfigurationException>(
            () => validator.LoadOrFailAsync(new FixedSource(null)));

        Assert.Contains(RuntimeConfigurationRefusalReason.ConfigurationMissing, exception.RefusalReasons);
        Assert.NotEmpty(exception.Validation.Detail);
    }

    [Fact]
    public async Task LoadOrFail_ReturnsTheConfiguration_OnSuccess()
    {
        var configuration = await Validator().LoadOrFailAsync(new FixedSource(Json("https://api.test.example")));

        Assert.Equal("ENV-DEV", configuration.EnvironmentLabel);
    }

    [Fact]
    public void AValidatorWithNoRequiredEndpoints_IsRefusedAtConstruction()
    {
        // A validator told to require nothing would pass every configuration, including an empty one —
        // a guard that cannot fail.
        Assert.Throws<ArgumentException>(() => new ClientRuntimeConfigurationValidator([], production: false));
    }

    [Fact]
    public void TheValidatorIsNotVacuouslyPassingOrRefusing()
    {
        var good = Validator().Parse(Json("https://api.test.example"));
        var bad = Validator().Parse(Json("https://api.test.example", schemaVersion: 2));

        Assert.True(good.IsValid);
        Assert.False(bad.IsValid);
    }

    private sealed class FixedSource(string? content) : IClientRuntimeConfigurationSource
    {
        public Task<string?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(content);
    }
}
