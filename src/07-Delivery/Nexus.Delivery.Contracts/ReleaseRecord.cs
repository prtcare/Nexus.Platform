using System.Text;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// <b>The immutable Release Bundle: the certified, identified set of bytes a promotion moves.</b>
///
/// <para>
/// <b>Why this is not <see cref="ReleaseBundle"/>.</b> The W9.1 bundle answers "which artifacts belong
/// together" and is registered in the bundle registry; it carries units, digests, shared contracts and
/// migration bindings. It deliberately carries nothing about how the release was produced or what it is
/// allowed to claim. This record answers the next question — <i>what is this release, what evidence stands
/// behind it, and what state is it in</i> — and it <b>carries the W9.1 bundle by reference</b> rather than
/// restating it. Two containers, one authority each; the alternative is the drift this estate has already
/// paid for twice.
/// </para>
///
/// <para>
/// <b>Immutable by construction, and immutable by policy.</b> There is no setter and no <c>with</c>-friendly
/// mutable member: every property is get-only and every collection is copied into a read-only list at
/// construction. Invariant I-1 applies to a release exactly as it applies to a bundle — a change to any
/// member produces a NEW release, never an edit. A release registry enforces the second half by refusing a
/// re-write of an existing <see cref="ReleaseId"/> whose record digest differs; see
/// <see cref="IReleaseRegistry"/>.
/// </para>
///
/// <para>
/// <b>No environment appears anywhere in it.</b> Not as a member, not inside the identity, not as a key.
/// The same record is promoted through DEV, TEST and PROD; environment is deployment state and lives on
/// <see cref="PromotionState"/>, which is per (bundle, environment). A reflection test asserts this absence
/// rather than trusting the paragraph.
/// </para>
///
/// <para>
/// <b>No secret value and no credential value appears anywhere in it.</b> Configuration appears as key
/// names, secrets as <see cref="SecretReference"/> names, and that type refuses a credential-shaped string
/// at construction. This is invariant L-1 carried into the release: a release record is a convenient place
/// to "record what we released with", which is precisely why it must be unable to.
/// </para>
/// </summary>
public sealed record ReleaseRecord
{
    /// <summary>Bumped when this contract's shape changes, so a stored bundle stays parseable.</summary>
    public const int CurrentSchemaVersion = 1;

    public const int MinSchemaVersion = 1;

    public const int MaxSchemaVersion = 1;

    public ReleaseRecord(
        int schemaVersion,
        ReleaseIdentity identity,
        BundleId bundleId,
        ReleaseEvidence evidence,
        DependencyLockState dependencyLock,
        IReadOnlyList<ContractVersion> contractVersions,
        MigrationAssessment migrations,
        RollbackMetadata rollback,
        HealthContract health,
        string configurationSchemaVersion,
        DateTimeOffset createdAt,
        IReadOnlyList<string>? configurationKeys = null,
        IReadOnlyList<SecretReference>? secretReferences = null,
        GovernedWorkReference? originatingWork = null)
    {
        if (schemaVersion < MinSchemaVersion || schemaVersion > MaxSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                $"This contract understands schema versions {MinSchemaVersion}–{MaxSchemaVersion}.");
        }

        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(bundleId);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(dependencyLock);
        ArgumentNullException.ThrowIfNull(migrations);
        ArgumentNullException.ThrowIfNull(rollback);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(contractVersions);

        if (string.IsNullOrWhiteSpace(configurationSchemaVersion))
        {
            throw new ArgumentException(
                "A release must name the configuration schema version it expects. Without it a consumer cannot tell a missing key from a renamed one.",
                nameof(configurationSchemaVersion));
        }

        if (!CredentialShape.IsNameShaped(configurationSchemaVersion))
        {
            throw new ArgumentException(
                "A configuration schema version must be name-shaped. It identifies a schema, never carries a value.",
                nameof(configurationSchemaVersion));
        }

        // A release only claims what its evidence supports. Stating this here rather than only in the
        // certification gate means an unsupportable record cannot be constructed at all, so the gate can
        // never be the only thing standing between a claim and a registry.
        if (evidence.ReproducibilityVerdict == ReproducibilityVerdict.NotPerformed)
        {
            throw new ArgumentException(
                "A release cannot be assembled from a build whose reproducibility was not established. A single build agreeing with itself is not reproducibility.",
                nameof(evidence));
        }

        foreach (var key in configurationKeys ?? [])
        {
            if (!CredentialShape.IsNameShaped(key))
            {
                throw new ArgumentException(
                    $"A configuration KEY must be name-shaped; '{Redact(key)}' is not. Invariant L-1 forbids a value in a release record.",
                    nameof(configurationKeys));
            }
        }

        foreach (var contract in contractVersions)
        {
            ArgumentNullException.ThrowIfNull(contract);
        }

        SchemaVersion = schemaVersion;
        Identity = identity;
        BundleId = bundleId;
        Evidence = evidence;
        DependencyLock = dependencyLock;
        ContractVersions = [.. contractVersions
            .OrderBy(c => c.ContractName, StringComparer.Ordinal)
            .ThenBy(c => c.Version, StringComparer.Ordinal)];
        Migrations = migrations;
        Rollback = rollback;
        Health = health;
        ConfigurationSchemaVersion = configurationSchemaVersion;
        CreatedAt = createdAt;
        ConfigurationKeys = [.. (configurationKeys ?? []).OrderBy(k => k, StringComparer.Ordinal)];
        SecretReferences = [.. (secretReferences ?? []).OrderBy(s => s.Value, StringComparer.Ordinal)];
        OriginatingWork = originatingWork;
    }

    public int SchemaVersion { get; }

    public ReleaseIdentity Identity { get; }

    /// <summary>
    /// The W9.1 bundle this release is made of. <b>The W9.1 contract is used, not replaced</b>: the artifact
    /// set, its shared contracts and its migration bindings live there and are registered there, and this
    /// record references them so that a release and its bundle cannot describe different bytes.
    /// </summary>
    public BundleId BundleId { get; }

    public ReleaseEvidence Evidence { get; }

    /// <summary>The dependency manifest state. Carried here rather than referenced, because "the .NET graph is pinned" is a release claim, not a build detail.</summary>
    public DependencyLockState DependencyLock { get; }

    public IReadOnlyList<ContractVersion> ContractVersions { get; }

    public MigrationAssessment Migrations { get; }

    public RollbackMetadata Rollback { get; }

    /// <summary>The readiness definition. Required: a release whose health surface is undeclared has no gate a promoter can read.</summary>
    public HealthContract Health { get; }

    /// <summary>The configuration schema this release expects at run time. An identifier, never a value.</summary>
    public string ConfigurationSchemaVersion { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Key NAMES only.</summary>
    public IReadOnlyList<string> ConfigurationKeys { get; }

    /// <summary>Reference NAMES only — the type makes a value unrepresentable.</summary>
    public IReadOnlyList<SecretReference> SecretReferences { get; }

    /// <summary>The governed work this release was performed for, when it is known. The outermost hop of the lineage chain.</summary>
    public GovernedWorkReference? OriginatingWork { get; }

    public ReleaseId ReleaseId => Identity.ReleaseId;

    public DeploymentUnitId UnitId => Identity.UnitId;

    public string Version => Identity.Version;

    public BuildId BuildId => Identity.BuildId;

    public IReadOnlyList<ReleaseArtifactIdentity> Artifacts => Identity.Artifacts;

    /// <summary>
    /// The canonical form of everything this record claims. This is the immutability key: two records with
    /// the same <see cref="ReleaseId"/> and the same canonical bytes are the same release, and any
    /// difference at all is a different one.
    ///
    /// <para>
    /// <b><see cref="CreatedAt"/> is deliberately excluded.</b> It is when a record was written, not what
    /// the record says, and including it would make the same release from two runs look like two releases.
    /// The registry compares this digest, and it is the reason a re-assembly of identical inputs is
    /// idempotent rather than a second release.
    /// </para>
    /// </summary>
    public string CanonicalForm()
    {
        var builder = new StringBuilder();

        void Field(string? value) => builder.Append(value ?? string.Empty).Append('\0');

        Field("nexus-release-record");
        Field(SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // The identity canonical form is embedded verbatim, so a change to any identity member is a change
        // here without a second list of members to keep in step.
        builder.Append(Identity.CanonicalForm()).Append('\0');

        Field(BundleId.Value);
        Evidence.AppendCanonicalForm(builder);
        Field(DependencyLock.ToString());
        Field(ConfigurationSchemaVersion);
        Field(Health.ReadinessPath);
        Field(Health.LivenessPath);

        foreach (var contract in ContractVersions)
        {
            contract.AppendCanonicalForm(builder);
        }

        Migrations.AppendCanonicalForm(builder);
        Rollback.AppendCanonicalForm(builder);

        foreach (var key in ConfigurationKeys)
        {
            Field(key);
        }

        foreach (var reference in SecretReferences)
        {
            Field(reference.Value);
        }

        if (OriginatingWork is not null)
        {
            OriginatingWork.AppendCanonicalForm(builder);
        }

        return builder.ToString();
    }

    /// <summary>The digest of <see cref="CanonicalForm"/>. What a release registry enforces immutability against.</summary>
    public ArtifactDigest ComputeRecordDigest()
        => ArtifactDigest.Compute(Encoding.UTF8.GetBytes(CanonicalForm()));

    /// <summary>True when this release would be the first accepted into an environment, with nothing to fall back to.</summary>
    public bool IsFirstRelease => Rollback.State == RollbackReferenceState.NoPreviousAcceptedRelease;

    private static string Redact(string? value)
        => string.IsNullOrEmpty(value) ? "(empty)" : $"(credential-shaped, {value.Length} chars, value withheld)";

    public override string ToString()
        => $"{ReleaseId} {UnitId}@{Version} · bundle={BundleId} · mig={Migrations.State} · rollback={Rollback.State}";
}
