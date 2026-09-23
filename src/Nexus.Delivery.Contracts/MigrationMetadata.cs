using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>How a target database's applied schema compares to what a bundle expects.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationCompatibility
{
    /// <summary>Applied history matches the bundle's migration set. Promotion may proceed.</summary>
    Match,

    /// <summary>Target is behind. Pending migrations are applied as part of the deployment.</summary>
    Behind,

    /// <summary>
    /// Target is ahead — migrated by a newer bundle. <b>Refused.</b> Proceeding would run an older
    /// application against a newer schema, which is the forward-only equivalent of a downgrade.
    /// </summary>
    Ahead,

    /// <summary>
    /// Same count, different identities. <b>Refused and escalated.</b> This is a manual
    /// investigation, not a deployment.
    /// </summary>
    Divergent
}

/// <summary>
/// The migration set a bundle expects, as an identity rather than as content.
///
/// <para>
/// W9.0 established that nothing in the estate declared which migrations an artifact expected to
/// find applied (<c>DATABASE_MIGRATION_INVENTORY.md</c> F-4.1). The migrations themselves travel
/// inside the unit artifact for EF units; what the bundle needs is the <b>identity of the set</b>,
/// so a promotion can compare it against a target's applied history.
/// </para>
///
/// <para>
/// W9.0 also established the reason this is not optional: two MarketSurvey migrations are
/// hand-authored inside an EF migration directory (round timestamps, no <c>.Designer.cs</c>), so the
/// model snapshot can diverge from the applied schema. A migration <i>set</i> identity plus a target
/// comparison is what makes that divergence visible.
/// </para>
/// </summary>
public sealed record MigrationMetadata
{
    public static MigrationMetadata None { get; } = new("none", []);

    public MigrationMetadata(string provider, IReadOnlyList<string> migrationIds)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("A migration set must name its provider.", nameof(provider));
        }

        ArgumentNullException.ThrowIfNull(migrationIds);

        Provider = provider;
        MigrationIds = [.. migrationIds];

        SetDigest = ComputeSetDigest(Provider, MigrationIds);
    }

    /// <summary>e.g. <c>ef-sqlserver</c>, <c>ef-postgresql</c>, <c>sql-ordered</c>, <c>none</c>.</summary>
    public string Provider { get; }

    /// <summary>Migration identities in apply order. For a <c>none</c> provider this is empty.</summary>
    public IReadOnlyList<string> MigrationIds { get; }

    /// <summary>Identity of the ordered set. Two bundles with the same set have the same digest.</summary>
    public ArtifactDigest SetDigest { get; }

    public bool IsNone => Provider == "none" && MigrationIds.Count == 0;

    private static ArtifactDigest ComputeSetDigest(string provider, IReadOnlyList<string> migrationIds)
    {
        // Deterministic by construction: the provider, then each migration id, each NUL-terminated
        // so that ["ab","c"] and ["a","bc"] cannot collide.
        var builder = new System.Text.StringBuilder(provider).Append('\0');
        foreach (var id in migrationIds)
        {
            builder.Append(id).Append('\0');
        }

        return ArtifactDigest.Compute(System.Text.Encoding.UTF8.GetBytes(builder.ToString()));
    }
}
