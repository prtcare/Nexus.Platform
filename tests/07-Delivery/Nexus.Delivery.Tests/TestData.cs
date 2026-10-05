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

    /// <summary>
    /// A token that <b>is</b> credential-shaped, <b>by construction</b> and not by luck.
    ///
    /// <para>
    /// <b>Why this exists beside <see cref="RandomAlnum"/>.</b> A positive fixture whose shape is drawn at
    /// random is a fixture that is not always the shape it claims. This estate measured the cost: an
    /// all-alphanumeric draw carries a digit-less result with probability <c>(52/62)^n</c> — 6.1% at
    /// <c>n=16</c>, 3.0% at <c>n=20</c>, 1.4% at <c>n=25</c> — and a digit-less, or single-case, token is a
    /// NAME under this estate's own rule (<see cref="CredentialShape.IsBareIdentifierShape"/>), so the
    /// scanner correctly declined it and <c>Assert.Single</c> failed on the <i>fixture</i> rather than on the
    /// rule. That is the worst shape a flake can take: a gate that is green on the runs that got lucky.
    /// </para>
    ///
    /// <para>
    /// So the classes are forced in at fixed positions — an upper-case letter, a digit, two lower-case letters
    /// — and the remainder repeats a single lower-case letter, which no draw can turn into a name. The value
    /// is assembled from parts rather than written as a literal, so this file never contains the
    /// credential-shaped string itself; see <see cref="RandomAlnum"/> for why that matters.
    /// </para>
    /// </summary>
    internal static string ValueShapedToken(int length)
    {
        if (length < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "A dense token needs at least four characters.");
        }

        return string.Concat("K7mz", new string('q', length - 4));
    }

    /// <summary>A generated value in the shape of a vendor-prefixed machine key: 35 characters, one unbroken run of 32.</summary>
    internal static string CredentialValue() => "sk-" + RandomAlnum(32);

    /// <summary>
    /// A value in the shape of a dense mixed-case token: 25 characters, no separators, and value-shaped
    /// deterministically — see <see cref="ValueShapedToken"/> for the flake this replaced.
    /// </summary>
    internal static string TokenValue() => ValueShapedToken(25);

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
    ///
    /// <para>
    /// <b>The release reference's protection is recorded as this estate actually has it</b>: the
    /// Owner-approved C-2 compensating control, verified, scoped to ENV-DEV and ENV-TEST. It deliberately does
    /// not say <c>ServerSideProtected</c> — no ruleset has been observed on these repositories — which is why
    /// a production promotion using this evidence is refused. A test that needs the ruleset case supplies it
    /// explicitly, so the one path that can carry ENV-PROD is stated rather than assumed.
    /// </para>
    /// </summary>
    internal static DeploymentGateEvidence HealthyEvidence(
        ReleaseBundle bundle,
        DeploymentAuthorityRole role = DeploymentAuthorityRole.DeliveryTeam)
        => new()
        {
            WorkingTreeIsDirty = false,
            ReleaseRefIsProtected = true,
            ReleaseReferenceProtection = ReleaseReferenceProtectionEvidence.CompensatingControl(),
            ReadinessObserved = true,
            // A healthy run produces BOTH observations. This member is here because Owner Decision 3 made
            // smoke load-bearing for a verification: a fixture that claimed "fully observed healthy
            // evidence" while omitting it was describing a run the gate must now refuse.
            SmokeObserved = true,
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

    /// <summary>
    /// A deployment attempt id for fixtures. <see cref="DeploymentPromoter"/> requires one, and requires it
    /// to name the environment the request targets — so this derives from the same default
    /// <see cref="Request"/> uses rather than from a constant that could drift away from it.
    ///
    /// <para>
    /// Named <c>DeploymentAttemptId</c> rather than <c>DeploymentId</c> so the call site does not read as a
    /// construction of the type it returns.
    /// </para>
    /// </summary>
    internal static Contracts.DeploymentId DeploymentAttemptId(
        DeploymentEnvironmentId? environment = null,
        int attempt = 1)
        => Contracts.DeploymentId.For(
            Contracts.ReleaseId.Parse("rel-2ec4c364727bcb74"),
            environment ?? DeploymentEnvironmentId.TestEnv,
            attempt);
}
