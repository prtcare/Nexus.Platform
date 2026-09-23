using System.Text;
using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>Whether a release requires a database migration, and whether that was established rather than assumed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationRequirementState
{
    /// <summary>
    /// The unit has no database schema to migrate. Recorded explicitly, never inferred from an empty list:
    /// "no migrations were found" and "nobody looked" produce the same empty collection and demand
    /// opposite responses.
    /// </summary>
    NoDatabaseMigration,

    /// <summary>A migration set applies. The ids, the version span and the compatibility metadata are carried.</summary>
    MigrationsRequired,

    /// <summary>
    /// Whether a migration applies could not be established. <b>This is a distinct state, not a failure of
    /// the release.</b> W9.0 measured that nothing in this estate declared which migrations an artifact
    /// expected to find applied, and two MarketSurvey migrations are hand-authored inside an EF migration
    /// directory, so the model snapshot can drift. Recording "unknown" honestly is the only state that
    /// makes the gap visible; defaulting to "none" would convert an unmeasured risk into a false assurance.
    /// </summary>
    Unknown
}

/// <summary>How a migration could be undone, which decides whether a rollback is even available.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationReversibility
{
    /// <summary>Not established. The honest default; no suite in this estate has ever exercised a Down migration (W9.0 F-4.4).</summary>
    Unknown,

    /// <summary>A Down path exists and has been exercised. Rare here, and it is the only case where an actual reversion is available.</summary>
    Reversible,

    /// <summary>
    /// No Down path, or a Down path that has never been run. The remedy is a forward fix — a new migration
    /// that corrects the schema — not a reversion. This is the classification the estate's existing
    /// migrations are expected to fall into, and it is recorded rather than assumed.
    /// </summary>
    ForwardFixOnly
}

/// <summary>
/// What a release expects of its target database, established by inspection rather than by convention.
///
/// <para>
/// <b>Nothing here runs a migration.</b> W9.3 records the state; W9.4+ acts on it. The distinction matters
/// because a release pipeline that migrates as a side effect of describing itself cannot be asked what it
/// would do without doing it.
/// </para>
/// </summary>
public sealed record MigrationAssessment
{
    private MigrationAssessment(
        MigrationRequirementState state,
        MigrationMetadata metadata,
        string? fromVersion,
        string? toVersion,
        MigrationCompatibility? compatibility,
        bool backupRequired,
        MigrationReversibility reversibility,
        string basis)
    {
        State = state;
        Metadata = metadata;
        FromVersion = fromVersion;
        ToVersion = toVersion;
        Compatibility = compatibility;
        BackupRequired = backupRequired;
        Reversibility = reversibility;
        Basis = basis;
    }

    public MigrationRequirementState State { get; }

    /// <summary>The migration set identity. <see cref="MigrationMetadata.None"/> when no migration applies.</summary>
    public MigrationMetadata Metadata { get; }

    /// <summary>The schema version the target is expected to be at before this release. Null when not established.</summary>
    public string? FromVersion { get; }

    /// <summary>The schema version this release brings the target to. Null when not established.</summary>
    public string? ToVersion { get; }

    /// <summary>How the target's applied set compares. Null when there is no migration or it was not established.</summary>
    public MigrationCompatibility? Compatibility { get; }

    /// <summary>
    /// True when the target must be backed up before the release. For a hand-authored EF migration inside a
    /// directory whose model snapshot can drift, this is true whenever a migration applies at all.
    /// </summary>
    public bool BackupRequired { get; }

    public MigrationReversibility Reversibility { get; }

    /// <summary>What the classification was established from. A state with no basis is an assertion.</summary>
    public string Basis { get; }

    public bool IsNone => State == MigrationRequirementState.NoDatabaseMigration;

    /// <summary>
    /// Records that the unit has no database to migrate. Requires the basis, because "no migration" is a
    /// finding and a finding must say what it was found by.
    /// </summary>
    public static MigrationAssessment NoDatabaseMigration(string basis)
    {
        if (string.IsNullOrWhiteSpace(basis))
        {
            throw new ArgumentException(
                "Recording NO_DATABASE_MIGRATION requires the basis it was established from. The empty collection and the unperformed check look identical without it.",
                nameof(basis));
        }

        return new MigrationAssessment(
            MigrationRequirementState.NoDatabaseMigration,
            MigrationMetadata.None,
            null,
            null,
            null,
            backupRequired: false,
            MigrationReversibility.Unknown,
            basis);
    }

    /// <summary>
    /// Records that a migration set applies.
    /// </summary>
    /// <param name="compatibility">
    /// How the target's applied set compares — <b>null when no target has been inspected</b>, which is the
    /// honest value at release time. Compatibility is a fact about a target, and W9.3 has none: DEV is
    /// W9.4's. Supplying a value here would be a stage reporting a comparison it never made.
    /// </param>
    public static MigrationAssessment Required(
        MigrationMetadata metadata,
        string? fromVersion,
        string? toVersion,
        MigrationCompatibility? compatibility,
        bool backupRequired,
        MigrationReversibility reversibility,
        string basis)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (metadata.IsNone)
        {
            throw new ArgumentException(
                "A 'migrations required' assessment must name the migration set. Use NoDatabaseMigration when none applies.",
                nameof(metadata));
        }

        if (string.IsNullOrWhiteSpace(basis))
        {
            throw new ArgumentException("A migration assessment must say what it was established from.", nameof(basis));
        }

        return new MigrationAssessment(
            MigrationRequirementState.MigrationsRequired,
            metadata,
            fromVersion,
            toVersion,
            compatibility,
            backupRequired,
            reversibility,
            basis);
    }

    /// <summary>
    /// Records that whether a migration applies could not be established. Refuses an empty basis for the
    /// same reason: the state exists to carry the reason it is unknown.
    /// </summary>
    public static MigrationAssessment Unestablished(MigrationMetadata? metadata, string basis)
    {
        if (string.IsNullOrWhiteSpace(basis))
        {
            throw new ArgumentException(
                "An 'unknown' migration assessment must record why it could not be established.",
                nameof(basis));
        }

        return new MigrationAssessment(
            MigrationRequirementState.Unknown,
            metadata ?? MigrationMetadata.None,
            null,
            null,
            null,
            backupRequired: true,
            MigrationReversibility.Unknown,
            basis);
    }

    internal void AppendCanonicalForm(StringBuilder builder)
        => builder.Append(State).Append('\0')
                  .Append(Metadata.Provider).Append('\0')
                  .Append(Metadata.SetDigest).Append('\0')
                  .Append(FromVersion ?? string.Empty).Append('\0')
                  .Append(ToVersion ?? string.Empty).Append('\0')
                  .Append(Compatibility?.ToString() ?? string.Empty).Append('\0')
                  .Append(BackupRequired).Append('\0')
                  .Append(Reversibility).Append('\0')
                  .Append(Basis).Append('\0');

    public override string ToString() => State switch
    {
        MigrationRequirementState.NoDatabaseMigration => "NO_DATABASE_MIGRATION",
        MigrationRequirementState.MigrationsRequired => $"{Metadata.MigrationIds.Count} migration(s), {Reversibility}",
        _ => "UNKNOWN"
    };
}
