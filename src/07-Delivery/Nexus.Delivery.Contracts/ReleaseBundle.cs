namespace Nexus.Delivery.Contracts;

/// <summary>
/// The immutable, identified set of unit artifacts promoted as one thing.
///
/// <para>
/// <b>Why a bundle and not "an artifact".</b> An artifact is per-unit; a release is a set of units
/// that must move together, because a service and the contract library it consumes are versioned
/// against each other. Promoted independently, an environment can hold a combination that never
/// existed in a build — which is precisely the failure the build-once rule exists to prevent. So the
/// shared contract library is carried here as a first-class member, not as ambient state.
/// </para>
///
/// <para>
/// <b>Built once, never edited.</b> The manifest is written by the build. Promotion reads it and
/// never rewrites it; a change to any member produces a NEW bundle id. Invariant I-1.
/// </para>
/// </summary>
public sealed record ReleaseBundle
{
    /// <summary>Bumped when this contract's shape changes, so a stored manifest stays parseable and consumers do not all have to change on the same day. Same convention as <c>PipelineRunResult.SchemaVersion</c>.</summary>
    public const int CurrentSchemaVersion = 1;

    public const int MinSchemaVersion = 1;

    public const int MaxSchemaVersion = 1;

    public ReleaseBundle(
        int schemaVersion,
        BundleId bundleId,
        DateTimeOffset createdAt,
        ReleaseSource source,
        BuilderIdentity builder,
        IReadOnlyList<ReleaseArtifact> artifacts,
        IReadOnlyList<ReleaseArtifact>? sharedContracts = null,
        IReadOnlyList<UnitMigrationBinding>? migrations = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? configurationKeys = null,
        IReadOnlyDictionary<string, IReadOnlyList<SecretReference>>? secretReferences = null)
    {
        if (schemaVersion < MinSchemaVersion || schemaVersion > MaxSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                $"This contract understands schema versions {MinSchemaVersion}–{MaxSchemaVersion}.");
        }

        ArgumentNullException.ThrowIfNull(bundleId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(builder);

        if (artifacts is null || artifacts.Count == 0)
        {
            throw new ArgumentException(
                "A bundle must carry at least one artifact. A partial bundle is not a release.",
                nameof(artifacts));
        }

        var duplicate = artifacts
            .GroupBy(a => a.UnitId)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"A bundle carries at most one artifact per unit; '{duplicate.Key}' appears {duplicate.Count()} times.",
                nameof(artifacts));
        }

        foreach (var artifact in artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
        }

        SchemaVersion = schemaVersion;
        BundleId = bundleId;
        CreatedAt = createdAt;
        Source = source;
        Builder = builder;
        Artifacts = [.. artifacts];
        SharedContracts = [.. sharedContracts ?? []];
        Migrations = [.. migrations ?? []];
        ConfigurationKeys = configurationKeys is null
            ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            : new Dictionary<string, IReadOnlyList<string>>(
                configurationKeys.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);
        SecretReferences = secretReferences is null
            ? new Dictionary<string, IReadOnlyList<SecretReference>>(StringComparer.Ordinal)
            : new Dictionary<string, IReadOnlyList<SecretReference>>(
                secretReferences.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    public int SchemaVersion { get; }

    public BundleId BundleId { get; }

    public DateTimeOffset CreatedAt { get; }

    public ReleaseSource Source { get; }

    public BuilderIdentity Builder { get; }

    /// <summary>The units moved together by this release. At least one; at most one per unit id.</summary>
    public IReadOnlyList<ReleaseArtifact> Artifacts { get; }

    /// <summary>
    /// Shared contract libraries this release pins — <c>Nexus.ProductCore.Contracts</c> and siblings.
    /// Carried so that a service and the contract it was compiled against cannot drift apart in
    /// promotion: they are one release or they are none.
    /// </summary>
    public IReadOnlyList<ReleaseArtifact> SharedContracts { get; }

    public IReadOnlyList<UnitMigrationBinding> Migrations { get; }

    /// <summary>The SCHEMA of what may differ per environment. Keys per unit — never a value.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ConfigurationKeys { get; }

    /// <summary>Reference names per unit. A value is unrepresentable: <see cref="SecretReference"/> refuses one.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SecretReference>> SecretReferences { get; }

    /// <summary>All artifacts including shared contracts, for digest comparison across environments.</summary>
    public IEnumerable<ReleaseArtifact> AllArtifacts => Artifacts.Concat(SharedContracts);

    public bool TryGetArtifact(DeploymentUnitId unitId, out ReleaseArtifact? artifact)
    {
        ArgumentNullException.ThrowIfNull(unitId);
        artifact = AllArtifacts.FirstOrDefault(a => a.UnitId == unitId);
        return artifact is not null;
    }
}

/// <summary>Binds a unit to the migration set it expects to find applied in its target.</summary>
public sealed record UnitMigrationBinding(DeploymentUnitId UnitId, MigrationMetadata Migrations);
