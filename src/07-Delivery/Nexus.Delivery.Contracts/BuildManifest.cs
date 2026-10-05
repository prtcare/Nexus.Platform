namespace Nexus.Delivery.Contracts;

/// <summary>
/// The machine-readable, immutable record of one build.
///
/// <para>
/// <b>Why the constructor refuses an incomplete manifest rather than accepting one and letting a reader
/// notice.</b> Task 2 lists what a manifest must contain; the failure mode of an optional field is that
/// it is absent in exactly the build whose provenance someone later needs. So the required members are
/// required by the type — an artifact list, a secret-scan result and a test verdict cannot be omitted —
/// and <see cref="MissingRequiredElements"/> covers what can only be judged in combination: whether the
/// reproducibility comparison ran, whether the scan covered the whole input set, and whether every
/// artifact carries a hash and a size.
/// </para>
///
/// <para>
/// <b>Nothing environment-shaped appears.</b> No environment name, no endpoint, no configuration value,
/// no secret value. The manifest may name the <i>keys</i> the artifact accepts at runtime
/// (<see cref="RuntimeConfigurationKeys"/>) because that is a property of the application's interface,
/// not of any environment — and it is what lets a promoter know what must be supplied without the
/// manifest ever carrying what was supplied.
/// </para>
/// </summary>
public sealed record BuildManifest
{
    public const int CurrentSchemaVersion = 1;

    public const int MinSchemaVersion = 1;

    public const int MaxSchemaVersion = 1;

    public BuildManifest(
        int schemaVersion,
        BuildIdentity identity,
        IReadOnlyList<PackagedArtifact> artifacts,
        BuildTestEvidence tests,
        SecretScanEvidence secretScan,
        ReproducibilityEvidence reproducibility,
        ProvenanceRecord provenance,
        DateTimeOffset builtAt,
        IReadOnlyList<string>? runtimeConfigurationKeys = null)
    {
        if (schemaVersion < MinSchemaVersion || schemaVersion > MaxSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                $"This contract understands schema versions {MinSchemaVersion}–{MaxSchemaVersion}.");
        }

        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(tests);
        ArgumentNullException.ThrowIfNull(secretScan);
        ArgumentNullException.ThrowIfNull(reproducibility);
        ArgumentNullException.ThrowIfNull(provenance);

        if (artifacts is null || artifacts.Count == 0)
        {
            throw new ArgumentException("A build manifest must record at least one artifact.", nameof(artifacts));
        }

        foreach (var artifact in artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
        }

        var duplicate = artifacts.GroupBy(a => a.ArtifactId.Value, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"An artifact id appears more than once in the manifest: '{duplicate.Key}'.", nameof(artifacts));
        }

        foreach (var key in runtimeConfigurationKeys ?? [])
        {
            if (!CredentialShape.IsNameShaped(key))
            {
                throw new ArgumentException(
                    $"A runtime configuration key must be name-shaped; '{key}' is not. A manifest carries key NAMES, never values.",
                    nameof(runtimeConfigurationKeys));
            }
        }

        SchemaVersion = schemaVersion;
        Identity = identity;
        Artifacts = [.. artifacts.OrderBy(a => a.ArtifactId.Value, StringComparer.Ordinal)];
        Tests = tests;
        SecretScan = secretScan;
        Reproducibility = reproducibility;
        Provenance = provenance;
        BuiltAt = builtAt;
        RuntimeConfigurationKeys = [.. (runtimeConfigurationKeys ?? []).OrderBy(k => k, StringComparer.Ordinal)];
    }

    public int SchemaVersion { get; }

    public BuildIdentity Identity { get; }

    public IReadOnlyList<PackagedArtifact> Artifacts { get; }

    public BuildTestEvidence Tests { get; }

    public SecretScanEvidence SecretScan { get; }

    public ReproducibilityEvidence Reproducibility { get; }

    public ProvenanceRecord Provenance { get; }

    public DateTimeOffset BuiltAt { get; }

    /// <summary>
    /// The configuration keys this application accepts at run time. Names only — this is the interface the
    /// artifact exposes, not anything supplied to it.
    /// </summary>
    public IReadOnlyList<string> RuntimeConfigurationKeys { get; }

    public BuildId BuildId => Identity.BuildId;

    /// <summary>
    /// The required elements that are absent or unusable, as a list of names. Empty means complete.
    ///
    /// <para>
    /// Distinct from the constructor's refusals: this judges combinations, and it is what the certification
    /// gate reads so that a refusal can name <i>which</i> element was missing rather than reporting
    /// "incomplete".
    /// </para>
    /// </summary>
    public IReadOnlyList<string> MissingRequiredElements()
    {
        var missing = new List<string>();

        if (Artifacts.Count == 0)
        {
            missing.Add("artifacts");
        }

        if (Artifacts.Any(a => string.IsNullOrWhiteSpace(a.ArtifactId.Value)))
        {
            missing.Add("artifactId");
        }

        if (Artifacts.Any(a => a.ContentDigest is null))
        {
            missing.Add("artifactHash");
        }

        if (Artifacts.Any(a => a.SizeBytes <= 0))
        {
            missing.Add("artifactSize");
        }

        if (string.IsNullOrWhiteSpace(Identity.BuildId.Value))
        {
            missing.Add("buildId");
        }

        if (Identity.Sources.Count == 0 || Identity.Sources.Any(s => string.IsNullOrWhiteSpace(s.CommitSha)))
        {
            missing.Add("sourceCommits");
        }

        if (string.IsNullOrWhiteSpace(Identity.BuildDefinitionVersion))
        {
            missing.Add("buildDefinitionVersion");
        }

        if (string.IsNullOrWhiteSpace(Identity.Toolchain.SdkVersion) || string.IsNullOrWhiteSpace(Identity.Toolchain.RuntimeVersion))
        {
            missing.Add("toolchainVersions");
        }

        if (Identity.DependencyLock is null)
        {
            missing.Add("dependencyManifestReference");
        }

        if (string.IsNullOrWhiteSpace(Tests.SuiteName))
        {
            missing.Add("testsExecuted");
        }

        if (Tests.Verdict is TestVerdict.NotRun or TestVerdict.NoTests)
        {
            missing.Add("testVerdict");
        }

        if (SecretScan.ScannedSubjectLabels.Count == 0)
        {
            missing.Add("secretScanResult");
        }

        if (Reproducibility.Verdict == ReproducibilityVerdict.NotPerformed)
        {
            missing.Add("reproducibilityResult");
        }

        if (string.IsNullOrWhiteSpace(Provenance.BuilderRunId))
        {
            missing.Add("provenance");
        }

        if (BuiltAt == default)
        {
            missing.Add("buildTimestamp");
        }

        return missing;
    }

    public bool IsComplete => MissingRequiredElements().Count == 0;

    public override string ToString()
        => $"{BuildId} · {Artifacts.Count} artifact(s) · tests={Tests.Verdict} · scan={SecretScan.Verdict} · repro={Reproducibility.Verdict}";
}
