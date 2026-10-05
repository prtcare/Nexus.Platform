using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Release;

/// <summary>
/// The inputs to one release run, as data.
///
/// <para>
/// <b>Every identity in this plan is quoted from W9.2's evidence, not invented here.</b> The build id, the
/// artifact id, the source commit and the artifact digest are the ones the build stage certified. A release
/// stage that derived its own would be a second build in all but name, and the whole point of the stage is
/// that the bytes were produced exactly once.
/// </para>
/// </summary>
public sealed record ReleasePlan
{
    [JsonPropertyName("unitId")] public string UnitId { get; init; } = string.Empty;
    [JsonPropertyName("artifactName")] public string ArtifactName { get; init; } = string.Empty;
    [JsonPropertyName("artifactVersion")] public string ArtifactVersion { get; init; } = string.Empty;
    [JsonPropertyName("artifactType")] public string ArtifactType { get; init; } = "dotnet-app";

    [JsonPropertyName("buildId")] public string BuildId { get; init; } = string.Empty;
    [JsonPropertyName("artifactSha256")] public string ArtifactSha256 { get; init; } = string.Empty;
    [JsonPropertyName("artifactBytes")] public long ArtifactBytes { get; init; }
    [JsonPropertyName("sourceCommit")] public string SourceCommit { get; init; } = string.Empty;

    [JsonPropertyName("bundleId")] public string BundleId { get; init; } = string.Empty;
    [JsonPropertyName("buildManifestReference")] public string BuildManifestReference { get; init; } = string.Empty;
    [JsonPropertyName("buildManifestDigest")] public string BuildManifestDigest { get; init; } = string.Empty;
    [JsonPropertyName("buildDefinitionVersion")] public string BuildDefinitionVersion { get; init; } = "build-definition-v1";
    [JsonPropertyName("buildConfiguration")] public string BuildConfiguration { get; init; } = "Release";

    // The certified build's identity inputs, quoted verbatim from its recorded manifest. They are carried
    // rather than defaulted because the build id is DERIVED from them: a release that supplied a
    // plausible-looking toolchain instead of the recorded one would derive a different build id, and the
    // driver refuses rather than proceeding on a manifest that disagrees with its own identity. That
    // refusal is the check which makes "this release names the certified build" verifiable rather than
    // asserted.
    [JsonPropertyName("toolchainSdkVersion")] public string ToolchainSdkVersion { get; init; } = string.Empty;
    [JsonPropertyName("toolchainRuntimeVersion")] public string ToolchainRuntimeVersion { get; init; } = string.Empty;
    [JsonPropertyName("toolchainOperatingSystem")] public string ToolchainOperatingSystem { get; init; } = string.Empty;
    [JsonPropertyName("dependencyLockNote")] public string DependencyLockNote { get; init; } = string.Empty;

    // The certified build's own evidence, quoted from its recorded manifest for the reason the manifest
    // digest is quoted: a release that re-derived these would be asserting facts about a build from its own
    // reconstruction rather than from the record. A hardcoded count that happens to match today is a count
    // that silently reports the wrong thing the moment the certified value changes.
    [JsonPropertyName("testSuiteName")] public string TestSuiteName { get; init; } = string.Empty;
    [JsonPropertyName("testsTotal")] public int TestsTotal { get; init; }
    [JsonPropertyName("testsPassed")] public int TestsPassed { get; init; }
    [JsonPropertyName("reproducibilityVerdict")] public string ReproducibilityVerdict { get; init; } = string.Empty;
    [JsonPropertyName("buildsCompared")] public int BuildsCompared { get; init; }
    [JsonPropertyName("builderRunId")] public string BuilderRunId { get; init; } = string.Empty;

    [JsonPropertyName("repositoryLabel")] public string RepositoryLabel { get; init; } = string.Empty;
    [JsonPropertyName("repositoryRoot")] public string RepositoryRoot { get; init; } = string.Empty;

    /// <summary>
    /// Where the certified artifact's bytes can be read from, so the store can be seeded with them.
    ///
    /// <para>
    /// This is a <b>publish</b>, not a build: the file is an output the build stage already produced and
    /// certified, and the store re-hashes it on the way in and refuses anything that does not hash to the
    /// certified digest. So this cannot be used to introduce different application bytes — it can only
    /// admit bytes that are already the certified ones, and it says so by refusing when they are not.
    /// </para>
    /// </summary>
    [JsonPropertyName("artifactSourcePath")] public string? ArtifactSourcePath { get; init; }

    [JsonPropertyName("contractVersions")] public ContractVersionPlan[] ContractVersions { get; init; } = [];

    [JsonPropertyName("migrationState")] public string MigrationState { get; init; } = "NoDatabaseMigration";
    [JsonPropertyName("migrationBasis")] public string MigrationBasis { get; init; } = string.Empty;
    [JsonPropertyName("migrationIds")] public string[] MigrationIds { get; init; } = [];
    [JsonPropertyName("migrationProvider")] public string MigrationProvider { get; init; } = string.Empty;
    [JsonPropertyName("migrationFromVersion")] public string? MigrationFromVersion { get; init; }
    [JsonPropertyName("migrationToVersion")] public string? MigrationToVersion { get; init; }
    [JsonPropertyName("migrationCompatibility")] public string? MigrationCompatibility { get; init; }

    [JsonPropertyName("rollbackState")] public string RollbackState { get; init; } = "NoPreviousAcceptedRelease";
    [JsonPropertyName("rollbackBasis")] public string RollbackBasis { get; init; } = string.Empty;
    [JsonPropertyName("previousReleaseId")] public string? PreviousReleaseId { get; init; }

    [JsonPropertyName("readinessPath")] public string ReadinessPath { get; init; } = "/health/ready";
    [JsonPropertyName("livenessPath")] public string? LivenessPath { get; init; } = "/health/live";

    [JsonPropertyName("configurationSchemaVersion")] public string ConfigurationSchemaVersion { get; init; } = "runtime-config-v1";
    [JsonPropertyName("configurationKeys")] public string[] ConfigurationKeys { get; init; } = [];
    [JsonPropertyName("secretReferences")] public string[] SecretReferences { get; init; } = [];

    [JsonPropertyName("governedWorkReference")] public string GovernedWorkReference { get; init; } = string.Empty;
    [JsonPropertyName("governedWorkTitle")] public string? GovernedWorkTitle { get; init; }
    [JsonPropertyName("governedWorkSeries")] public string? GovernedWorkSeries { get; init; }

    [JsonPropertyName("lineageId")] public string LineageId { get; init; } = string.Empty;

    [JsonPropertyName("releaseRefCommit")] public string ReleaseRefCommit { get; init; } = string.Empty;

    /// <summary>
    /// The remote the governed release reference is published to and re-observed from, as the caller names it
    /// (for example <c>origin</c>). Never resolved or defaulted by the driver: a remote chosen by the tool is a
    /// remote nobody chose.
    /// </summary>
    [JsonPropertyName("releaseRemote")] public string? ReleaseRemote { get; init; }

    /// <summary>
    /// The release this run acts on, when the run is one of the two bounded W9.4 acts rather than a full
    /// assembly.
    ///
    /// <para>
    /// <b>The full run never reads this.</b> A full run derives the release id from the manifest it assembles,
    /// and accepting an id from the plan would let a caller name a release the assembled identity does not
    /// produce. The two bounded acts are the opposite case: the release already exists, its id was derived
    /// when it was assembled, and there is nothing left to derive it from — so it is quoted, and every act
    /// that quotes it verifies the release against its registered digest before touching it.
    /// </para>
    /// </summary>
    [JsonPropertyName("existingReleaseId")] public string? ExistingReleaseId { get; init; }

    /// <summary>
    /// The root of the <b>governed release plan store</b> — the only authority for C-1 and C-2.
    ///
    /// <para>
    /// <b>This is the W9.4 change, and it is a removal rather than an addition.</b> Until W9.4 this plan file
    /// carried <c>credentialRotationConfirmed</c>, <c>releaseRefProtectionMode</c>, the two compensating-control
    /// booleans, <c>releaseRefProtectionScope</c> and <c>releaseRefServerSideProtectionVerified</c> as
    /// hand-editable members of an untracked JSON file with no writer, schema or review path. Those members are
    /// gone. The security state is read from a store that validates every write, versions every change, records
    /// before/after hashes and provenance, and refuses contradictions.
    /// </para>
    ///
    /// <para>
    /// <see cref="RetiredSecurityMembers"/> is refused if it appears in a run plan at all, so the old path
    /// cannot be reintroduced by adding a field back and hoping the driver still reads it.
    /// </para>
    /// </summary>
    [JsonPropertyName("governedPlanRoot")] public string GovernedPlanRoot { get; init; } = string.Empty;

    /// <summary>
    /// The release-plan members W9.4 retired, named so their reappearance is refused rather than ignored.
    ///
    /// <para>
    /// An ignored member is the dangerous outcome, not a harmless one: a run plan that carried
    /// <c>credentialRotationConfirmed: true</c> and had it silently ignored would look to its author like the
    /// security action had been recorded, while the gate read the governed store and found nothing. The refusal
    /// says which store actually holds the fact.
    /// </para>
    /// </summary>
    public static readonly string[] RetiredSecurityMembers =
    [
        "credentialRotationConfirmed",
        "releaseRefServerSideProtectionVerified",
        "releaseRefProtectionMode",
        "releaseRefCompensatingControlApproved",
        "releaseRefCompensatingControlVerified",
        "releaseRefProtectionScope"
    ];

    [JsonPropertyName("scanSubjects")] public ScanSubjectPlan[] ScanSubjects { get; init; } = [];
    [JsonPropertyName("expectedActiveInputLabels")] public string[] ExpectedActiveInputLabels { get; init; } = [];
    [JsonPropertyName("forbiddenArtifactMarkers")] public string[] ForbiddenArtifactMarkers { get; init; } = [];

    [JsonPropertyName("storeRoot")] public string StoreRoot { get; init; } = string.Empty;
    [JsonPropertyName("lineageRoot")] public string LineageRoot { get; init; } = string.Empty;
    [JsonPropertyName("evidenceDirectory")] public string EvidenceDirectory { get; init; } = string.Empty;

    /// <summary>
    /// The register of defects declared to block promotion beyond ENV-DEV, as the party preparing the run
    /// states it.
    ///
    /// <para>
    /// <b>Null means nobody declared one, and that refuses.</b> A promotion precondition requires that no
    /// unresolved defect blocks the deployment, and the estate's rule is that an unstated fact is not a
    /// satisfied one. The member is nullable and has no default value that passes, so the difference between
    /// <i>"we enumerated the register and it is empty"</i> and <i>"we never looked"</i> survives into the
    /// evidence — an empty array is the first, <see langword="null"/> is the second.
    /// </para>
    ///
    /// <para>
    /// It lives in the run plan rather than in the governed store because it is a fact about <i>this
    /// promotion attempt</i>, not about the release's security posture; the governed store holds C-1 and C-2,
    /// which are facts about the release.
    /// </para>
    /// </summary>
    [JsonPropertyName("blockingDefects")] public BlockingDefectPlan[]? BlockingDefects { get; init; }

    /// <summary>
    /// Where the environment being promoted <i>from</i> keeps the evidence this promotion reads.
    ///
    /// <para>
    /// <b>Supplied, never guessed.</b> A promotion into ENV-TEST rests on facts proven in ENV-DEV — that the
    /// release's rollback remedy has been exercised there — and that proof lives in the evidence root of the
    /// run that produced it. A driver that searched for it would either have to know every previous lane's
    /// directory (a hardcoded list that goes stale the moment a lane is re-run) or glob for a filename
    /// (which can find a different run's document and read it as this one's). Naming the directory is the
    /// same discipline the rest of this plan follows: a path a tool chose is a path nobody chose.
    /// </para>
    ///
    /// <para>
    /// Null when the promotion has no source environment to read from, which is the case for every lane
    /// that deploys rather than promotes.
    /// </para>
    /// </summary>
    [JsonPropertyName("sourceEnvironmentEvidenceDirectory")] public string? SourceEnvironmentEvidenceDirectory { get; init; }

    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ReleasePlan Load(string path)
    {
        var text = File.ReadAllText(path);

        // ---- The retired security members are refused, not ignored ------------------------------------
        // Read from the raw JSON rather than from the deserialized record, because the whole point is that a
        // member this contract no longer names must not pass unnoticed. A run plan that still carries them was
        // written against the pre-W9.4 authority, and its author would reasonably believe the security action
        // had been recorded — while the gate reads the governed store and finds nothing.
        using (var document = JsonDocument.Parse(text))
        {
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var retired in RetiredSecurityMembers)
                {
                    if (document.RootElement.TryGetProperty(retired, out _))
                    {
                        throw new InvalidOperationException(
                            $"The release plan at '{path}' carries the retired member '{retired}'. C-1 and C-2 are no longer "
                            + "recorded in a release-run plan: they are held in the governed release plan store, written only "
                            + "through its writer, and read from there by the gates. Remove the member and record the fact in "
                            + "the governed store instead — leaving it here would make two authorities for one decision.");
                    }
                }
            }
        }

        var plan = JsonSerializer.Deserialize<ReleasePlan>(text, Options)
            ?? throw new InvalidOperationException($"The release plan at '{path}' parsed to nothing.");

        if (string.IsNullOrWhiteSpace(plan.GovernedPlanRoot))
        {
            throw new InvalidOperationException(
                "A release plan must name the governed release plan store it will read C-1 and C-2 from. "
                + "There is no fallback: a run with no governed plan root cannot establish either security action.");
        }

        if (string.IsNullOrWhiteSpace(plan.UnitId) || string.IsNullOrWhiteSpace(plan.ArtifactName))
        {
            throw new InvalidOperationException("A release plan must name its unit and artifact.");
        }

        // The plan must quote a certified identity. A release plan that could invent one would make the
        // whole stage a rebuild with extra steps.
        if (string.IsNullOrWhiteSpace(plan.BuildId) || !Contracts.BuildId.IsValid(plan.BuildId))
        {
            throw new InvalidOperationException($"A release plan must quote a certified build id; '{plan.BuildId}' is not one.");
        }

        if (string.IsNullOrWhiteSpace(plan.ArtifactSha256) || !ArtifactDigest.TryParse(plan.ArtifactSha256, out _))
        {
            throw new InvalidOperationException("A release plan must quote the certified artifact's canonical sha256 digest.");
        }

        if (string.IsNullOrWhiteSpace(plan.SourceCommit))
        {
            throw new InvalidOperationException("A release plan must quote the certified source commit.");
        }

        // The certified manifest's digest, not one this stage can recompute. A release records it as its
        // build-evidence reference, and a reference whose digest was derived from a locally reconstructed
        // manifest would not match the manifest at the referenced location.
        if (string.IsNullOrWhiteSpace(plan.BuildManifestDigest) || !ArtifactDigest.TryParse(plan.BuildManifestDigest, out _))
        {
            throw new InvalidOperationException(
                "A release plan must quote the certified build manifest's canonical sha256 digest.");
        }

        if (string.IsNullOrWhiteSpace(plan.TestSuiteName) || string.IsNullOrWhiteSpace(plan.BuilderRunId))
        {
            throw new InvalidOperationException(
                "A release plan must quote the certified build's test suite and its builder run.");
        }

        if (!Enum.TryParse<ReproducibilityVerdict>(plan.ReproducibilityVerdict, out _))
        {
            throw new InvalidOperationException(
                $"'{plan.ReproducibilityVerdict}' is not a reproducibility verdict this contract understands.");
        }

        if (plan.BuildsCompared < 2)
        {
            // One build agreeing with itself is not reproducibility. A release that claimed it would be
            // recording the exact non-proof the W9.2 gate exists to refuse.
            throw new InvalidOperationException(
                $"A release plan must quote at least two compared builds; it quotes {plan.BuildsCompared}.");
        }

        if (string.IsNullOrWhiteSpace(plan.StoreRoot))
        {
            throw new InvalidOperationException("A release plan must name the artifact store it will verify against.");
        }

        if (plan.ScanSubjects.Length == 0 || plan.ExpectedActiveInputLabels.Length == 0)
        {
            throw new InvalidOperationException(
                "A release plan must name its scan subjects and the active inputs the scan is expected to cover. "
                + "A scan that covered nothing has established nothing.");
        }

        return plan;
    }

    internal ArtifactType ResolveArtifactType() => ArtifactType switch
    {
        "dotnet-app" => Contracts.ArtifactType.DotnetApplication,
        "dotnet-lib" => Contracts.ArtifactType.DotnetLibrary,
        "client-bundle" => Contracts.ArtifactType.StaticClientBundle,
        "package" => Contracts.ArtifactType.Package,
        "container-image" => Contracts.ArtifactType.ContainerImage,
        var other => throw new InvalidOperationException($"Unknown artifact type '{other}'.")
    };

    internal ArtifactId ResolveArtifactId()
        => Contracts.ArtifactId.For(
            Contracts.DeploymentUnitId.Parse(UnitId),
            ResolveArtifactType(),
            ArtifactName,
            ArtifactVersion);

    internal ArtifactDigest ResolveArtifactDigest() => ArtifactDigest.Parse(ArtifactSha256);

    internal BuildId ResolveBuildId() => Contracts.BuildId.Parse(BuildId);

    internal MigrationAssessment ResolveMigrations() => MigrationState switch
    {
        "NoDatabaseMigration" => MigrationAssessment.NoDatabaseMigration(MigrationBasis),

        "MigrationsRequired" => MigrationAssessment.Required(
            new MigrationMetadata(MigrationProvider, MigrationIds),
            MigrationFromVersion,
            MigrationToVersion,
            // Null unless the plan names one. No target exists to compare against at release time, and a
            // release that reported a compatibility it never measured would be inventing the one field the
            // deployment stage most depends on.
            MigrationCompatibility is null
                ? null
                : Enum.Parse<Contracts.MigrationCompatibility>(MigrationCompatibility),
            backupRequired: true,
            MigrationReversibility.ForwardFixOnly,
            MigrationBasis),

        _ => MigrationAssessment.Unestablished(null, MigrationBasis)
    };

    internal RollbackMetadata ResolveRollback()
        => RollbackState == "PreviousAcceptedReleaseExists"
            ? RollbackMetadata.ToPreviousRelease(
                Contracts.ReleaseId.Parse(PreviousReleaseId
                    ?? throw new InvalidOperationException("A release plan naming a previous release must carry its id.")),
                crossesMigrationBoundary: MigrationState == "MigrationsRequired",
                rehearsed: false,
                RollbackBasis)
            : RollbackMetadata.NoPreviousAcceptedRelease(
                crossesMigrationBoundary: MigrationState == "MigrationsRequired",
                RollbackBasis);
}

public sealed record ContractVersionPlan
{
    [JsonPropertyName("contractName")] public string ContractName { get; init; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;
    [JsonPropertyName("sourceRepository")] public string? SourceRepository { get; init; }

    internal ContractVersion ToContractVersion() => new(ContractName, Version, SourceRepository);
}

/// <summary>
/// One declared defect that blocks promotion, as the run plan states it.
///
/// <para>
/// Every member is required except <see cref="Resolution"/>, and <see cref="Resolution"/> is required
/// <i>whenever <see cref="IsResolved"/> is true</i> — enforced by
/// <see cref="Nexus.Delivery.Contracts.DeploymentBlockingDefect.IsDischarged"/>, so a defect marked
/// resolved with nothing beside it is treated as unresolved rather than as a pass.
/// </para>
/// </summary>
public sealed record BlockingDefectPlan
{
    [JsonPropertyName("defectId")] public string DefectId { get; init; } = string.Empty;
    [JsonPropertyName("summary")] public string Summary { get; init; } = string.Empty;
    [JsonPropertyName("isResolved")] public bool IsResolved { get; init; }
    [JsonPropertyName("resolution")] public string? Resolution { get; init; }

    /// <summary>Projects this plan member onto the contract type the promotion gate reads.</summary>
    public Contracts.DeploymentBlockingDefect ToDefect() => new(DefectId, Summary, IsResolved, Resolution);
}

public sealed record ScanSubjectPlan
{
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    [JsonPropertyName("root")] public string Root { get; init; } = string.Empty;
    [JsonPropertyName("classification")] public string Classification { get; init; } = "ActiveBuildInput";
    [JsonPropertyName("quarantineJustification")] public string? QuarantineJustification { get; init; }

    internal ScanSubject ToSubject() => new(
        Label,
        Root,
        string.Equals(Classification, "QuarantinedHistorical", StringComparison.OrdinalIgnoreCase)
            ? ScanSubjectClass.QuarantinedHistorical
            : ScanSubjectClass.ActiveBuildInput,
        QuarantineJustification);
}
