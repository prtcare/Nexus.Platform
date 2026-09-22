using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Tests;

/// <summary>
/// Builders for the records the tests need. Kept in one place so that a change to a contract's shape is
/// a change in one file rather than in every test.
/// </summary>
internal static class TestData
{
    internal const string CleanCommit = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";

    /// <summary>
    /// A random alphanumeric string. Used to build credential-shaped fixtures at RUN TIME.
    ///
    /// <para>
    /// These are generated rather than written as literals for a measured reason: this repository's own
    /// secret-scan gate (<c>SecretScanGateTests</c>) scans this tree, and the first draft of these tests
    /// failed that gate with five findings, all of them credential-shaped literals in test files. The
    /// gate was right — a literal that looks like a credential is indistinguishable from one, and "it is
    /// only a fixture" is not something a scanner can verify. No allowlist was added, because a
    /// suppression list is exactly where a real credential would hide.
    /// </para>
    /// </summary>
    internal static string RandomAlnum(int length)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var chars = new char[length];

        for (var i = 0; i < length; i++)
        {
            chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        }

        return new string(chars);
    }

    internal static string RandomUpperAlnum(int length)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var chars = new char[length];

        for (var i = 0; i < length; i++)
        {
            chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>A generated value in the shape of a vendor-prefixed machine key: 35 characters, one unbroken run of 32.</summary>
    internal static string CredentialValue() => "sk-" + RandomAlnum(32);

    /// <summary>A generated value in the shape of a dense mixed-case token: 25 characters, no separators.</summary>
    internal static string TokenValue() => RandomAlnum(25);

    internal static ArtifactDigest Digest(char fill)
        => ArtifactDigest.Parse($"sha256:{new string(fill, 64)}");

    internal static DeploymentUnitId ApiUnit => DeploymentUnitId.Parse("nexus.developer.api");

    internal static DeploymentUnit ServiceUnit(DeploymentUnitId? id = null, string? exclusionReason = null)
        => new(
            id ?? ApiUnit,
            DeploymentUnitKind.Service,
            "Products/Developer",
            "src/Nexus.Developer.Api/Nexus.Developer.Api.csproj",
            PackagingMedium.Container,
            new HealthContract("/health/ready", "/health/live"),
            ["ASPNETCORE_ENVIRONMENT", "ConnectionStrings__NexusDeveloper"],
            [SecretReference.Parse("NEXUS_OPENAI_API_KEY")],
            migrations: null,
            exclusionReason: exclusionReason);

    internal static ReleaseArtifact Artifact(DeploymentUnitId? id = null, char digestFill = 'a', long size = 1024)
        => new(id ?? ApiUnit, Digest(digestFill), size);

    internal static ReleaseBundle Bundle(
        string bundleId = "nexus-2026.09.22-7f3a1c",
        bool dirty = false,
        int schemaVersion = ReleaseBundle.CurrentSchemaVersion,
        string? commit = null)
        => new(
            schemaVersion,
            BundleId.Parse(bundleId),
            DateTimeOffset.Parse("2026-09-22T14:02:11+00:00", System.Globalization.CultureInfo.InvariantCulture),
            new ReleaseSource(commit ?? CleanCommit, "w9.1/release", dirty),
            new BuilderIdentity("10.0.302", "run-42", "linux"),
            [Artifact()],
            sharedContracts: [Artifact(DeploymentUnitId.Parse("platform.productcore.contracts"), 'b', 2048)],
            migrations: [new UnitMigrationBinding(ApiUnit, new MigrationMetadata("ef-sqlserver", ["20260827064146_InitialSqlSchema"]))],
            configurationKeys: new Dictionary<string, IReadOnlyList<string>> { ["nexus.developer.api"] = ["ASPNETCORE_ENVIRONMENT"] },
            secretReferences: new Dictionary<string, IReadOnlyList<SecretReference>>
            {
                ["nexus.developer.api"] = [SecretReference.Parse("NEXUS_OPENAI_API_KEY")]
            });

    /// <summary>
    /// Evidence describing a fully observed, healthy, authorized promotion. Individual tests then break
    /// exactly one member — which is what makes each refusal assertion attribute the refusal to its cause
    /// rather than to a builder that happened to be incomplete.
    /// </summary>
    internal static DeploymentGateEvidence HealthyEvidence(
        ReleaseBundle bundle,
        DeploymentAuthorityRole role = DeploymentAuthorityRole.DeliveryTeam)
        => new()
        {
            WorkingTreeIsDirty = false,
            ReleaseRefIsProtected = true,
            ReadinessObserved = true,
            ObservedDigests = bundle.AllArtifacts.ToDictionary(a => a.UnitId, a => a.Digest),
            MigrationCompatibility = Contracts.MigrationCompatibility.Match,
            ConfigurationSchemaSatisfied = true,
            Authorization = DeploymentAuthorization.For(role, "delivery-bot", DateTimeOffset.UnixEpoch),
            Reason = "W9.1 promotion proof.",
            PreviousBundleAvailable = true,
            RollbackRehearsed = true
        };

    internal static PromotionRequest Request(
        ReleaseBundle bundle,
        DeploymentTransition transition,
        PromotionState from,
        DeploymentGateEvidence? evidence = null,
        DeploymentEnvironmentId? environment = null)
        => new(
            bundle,
            environment ?? DeploymentEnvironmentId.TestEnv,
            from,
            transition,
            evidence ?? HealthyEvidence(bundle));
}
