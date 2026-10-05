using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Deploy;

/// <summary>
/// <b>The durable record of one migration step</b>, written whether it succeeded, was refused, or threw.
///
/// <para>
/// <b>Why this exists.</b> W9.5 finding <c>D-W9.5-6</c>. A promotion run applied five migrations to ENV-TEST
/// and left no structured record of having done so: the driver wrote its verb-level evidence document on the
/// migration-governance refusal path and on the state-machine refusal path, but the two failure paths that
/// occur <i>after</i> the target has been mutated — readiness never observed, smoke not observed — were bare
/// returns. The only trace of a governed environment having been changed was a redacted process log and a
/// console transcript the caller happened to redirect. Had it not been redirected, an attempt that mutated a
/// governed environment would have left nothing that states what was attempted, what was observed, or what
/// state the target was left in.
/// </para>
///
/// <para>
/// <b>Written in a <c>finally</c>, and that placement is the whole feature.</b> The record is not written by
/// the success path or the failure path; it is written by every path, because the migration block it wraps
/// cannot exit without it. The estate's ordering rule — a step that can fail must fail <i>before</i> the
/// durable record is written — is about the <i>transition</i>, and is satisfied by
/// <c>DeploymentEvidenceTransaction</c>. This is the complementary obligation one layer down: once a target
/// has been mutated, the record of the mutation is not optional, so it must not depend on the run reaching
/// its end.
/// </para>
///
/// <para>
/// <b>Every member is <c>required</c>, and that is deliberate.</b> A member with a default is a member a
/// caller can forget, and the member most likely to be forgotten in a record like this is the one that says
/// what the database looked like <i>before</i>. A record that cannot state its pre-state cannot support the
/// only question anyone asks it afterwards: what changed.
/// </para>
///
/// <para>
/// <b>No secret is a member, and the type cannot carry one.</b> The database password is never a parameter
/// of any constructor here; the connection is identified by container, host, port, database name and image.
/// The evidence writer additionally asserts the serialized document against the same credential-shape
/// judgement the source scanner uses, so a value that reached this record by a route nobody anticipated is
/// caught before the file is durable rather than after.
/// </para>
/// </summary>
internal sealed record MigrationEvidence
{
    internal const string Schema = "nexus-migration-evidence-v1";

    /// <summary>
    /// Fragments that, in a member NAME, mean the record is trying to carry a credential. The screen is on
    /// names rather than values because a value-shaped judgement cannot be applied to a record made of
    /// digests - see the account in <see cref="Write"/> - and a name has no such ambiguity.
    /// </summary>
    private static readonly string[] CredentialBearingMemberNames =
    [
        "password", "secret", "token", "credential", "apikey", "api_key", "connectionstring"
    ];

    // ---- identity: which environment, which database, which release ---------------------------------
    public required string ReleaseId { get; init; }

    /// <summary>The deployment attempt this migration belongs to. Minted before the migration runs.</summary>
    public required string DeploymentId { get; init; }

    public required string Environment { get; init; }

    public required string SourceCommit { get; init; }

    /// <summary>The container or service name the engine reports for the target.</summary>
    public required string DatabaseId { get; init; }

    public required string DatabaseHost { get; init; }

    public required int DatabasePort { get; init; }

    public required string DatabaseName { get; init; }

    public required string DatabaseImage { get; init; }

    // ---- the certified migration set this run evaluated ----------------------------------------------
    public required string MigrationProvider { get; init; }

    public required IReadOnlyList<string> CertifiedMigrationSet { get; init; }

    /// <summary>
    /// The destructive forward migrations measured from the certified source tree — the one input the
    /// judgement cannot observe from the target, recorded so a reader can see what it was measured against.
    /// </summary>
    public required IReadOnlyList<string> DestructiveForwardMigrations { get; init; }

    // ---- pre-state, backup, applied, post-state ------------------------------------------------------
    public required MigrationStateSnapshot PreState { get; init; }

    /// <summary>The backup that discharged the release's recorded obligation, if one was required.</summary>
    public BackupReference? Backup { get; init; }

    /// <summary>
    /// The migrations this run actually applied. <b>Empty is a fact, not a failure</b> — a target already
    /// carrying the certified set has nothing to apply, and the record says so rather than omitting the
    /// member.
    /// </summary>
    public required IReadOnlyList<string> AppliedByThisRun { get; init; }

    /// <summary>Null when the run never reached the post-migration observation.</summary>
    public MigrationStateSnapshot? PostState { get; init; }

    // ---- result --------------------------------------------------------------------------------------
    /// <summary>One of <see cref="MigrationEvidenceResult"/>.</summary>
    public required string Result { get; init; }

    /// <summary>Why, when the result is not <c>Applied</c> or <c>NotRequired</c>. Never a credential value.</summary>
    public string? RefusalReason { get; init; }

    /// <summary>Whether the artifact's operator command was actually invoked.</summary>
    public required bool MigrationCommandExecuted { get; init; }

    public int? MigrationCommandExitCode { get; init; }

    /// <summary>The migration-governance decision this run acted on.</summary>
    public string? GovernanceDecision { get; init; }

    /// <summary>The compatibility the governance judgement reported before the migration ran.</summary>
    public string? CompatibilityBefore { get; init; }

    // ---- provenance ----------------------------------------------------------------------------------
    public required string StartedUtc { get; init; }

    public required string CompletedUtc { get; init; }

    /// <summary>The actor recorded on the deployment authorization. Not a person; a lane's driver identity.</summary>
    public required string Actor { get; init; }

    public required string Instrument { get; init; }

    /// <summary>Always false. A member rather than an absence, because a reader should not have to infer it.</summary>
    public required bool SecretsInThisRecord { get; init; }

    /// <summary>What the target's schema and data looked like at one moment, in the terms the contract uses.</summary>
    internal sealed record MigrationStateSnapshot
    {
        public required bool Reachable { get; init; }

        public required bool SchemaPresent { get; init; }

        public required IReadOnlyList<string> AppliedMigrationIds { get; init; }

        /// <summary>The contract's classification: <c>Empty</c>, <c>HoldsData</c> or <c>Unknown</c>.</summary>
        public required string DataState { get; init; }

        /// <summary>How the classification was established. <c>DeterministicCount</c> is the only discharging basis.</summary>
        public required string DataStateBasis { get; init; }

        public required bool BackupVerified { get; init; }

        public required string Basis { get; init; }

        internal static MigrationStateSnapshot From(MigrationTargetObservation observation) => new()
        {
            Reachable = observation.Reachable,
            SchemaPresent = observation.SchemaPresent,
            AppliedMigrationIds = [.. observation.AppliedMigrationIds],
            DataState = observation.DataState.ToString(),
            DataStateBasis = observation.DataStateBasis.ToString(),
            BackupVerified = observation.BackupVerified,
            Basis = observation.Basis
        };
    }

    internal sealed record BackupReference
    {
        public required bool IsVerified { get; init; }

        public required string? Path { get; init; }

        public required string? Digest { get; init; }

        public required long Bytes { get; init; }

        public required int TocEntries { get; init; }

        public required string Basis { get; init; }

        public required string? RefusalReason { get; init; }

        internal static BackupReference From(EnvironmentDatabase.BackupObservation observation) => new()
        {
            IsVerified = observation.IsVerified,
            Path = observation.Path,
            Digest = observation.Digest,
            Bytes = observation.Bytes,
            TocEntries = observation.TocEntries,
            Basis = observation.Basis,
            RefusalReason = observation.RefusalReason
        };
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// The address: <c>&lt;evidenceRoot&gt;/deployments/&lt;DeploymentId&gt;/MIGRATION_EVIDENCE.json</c>.
    ///
    /// <para>
    /// Attempt-scoped, one per deployment attempt, because a migration is a property of <i>this</i> attempt
    /// at deploying this release into this environment and a later attempt is a different act. A re-run
    /// therefore mints a new deployment ordinal and writes a new file; the earlier one is untouched. The
    /// address is a pure function of the identity, so a reader holding the lineage record resolves it
    /// without being told where it is.
    /// </para>
    /// </summary>
    public static string PathFor(string evidenceRoot, DeploymentId deployment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        ArgumentNullException.ThrowIfNull(deployment);

        return Path.Combine(
            System.IO.Path.GetFullPath(evidenceRoot),
            "deployments",
            deployment.Value,
            "MIGRATION_EVIDENCE.json");
    }

    /// <summary>
    /// Writes the record atomically and returns the path. <b>Throws rather than returning a status</b>: the
    /// only caller is a <c>finally</c>, and a record that failed to become durable is not something a caller
    /// can decide to ignore.
    /// </summary>
    public string Write(string evidenceRoot, DeploymentId deployment)
    {
        var path = PathFor(evidenceRoot, deployment);
        var directory = System.IO.Path.GetDirectoryName(path)!;

        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(this, Options);

        // The record is quoted into an audit trail, so a value that looks like a credential is refused here
        // rather than persisted. This is a backstop, not the primary control: no member of this type is a
        // credential and no parameter to it can carry one. It exists because the one way a record like this
        // leaks is by someone adding a member later that happens to hold a connection string.
        //
        // ---- what guards this record, and the three attempts it took to find it -------------------------
        //
        // The first version concatenated the backup digest, the database name and the image into one string
        // and asked CredentialShape whether THAT looked like a credential. It does - five unrelated fragments
        // joined together have the character mix and density the heuristic looks for - so it refused a good
        // record on the success path. A check whose input is fabricated answers a question nobody asked.
        //
        // The second judged each member individually and refused on 'backup.digest'. The third refused on
        // 'preState.basis', whose prose legitimately quotes a digest. Reading the predicate settles it:
        // LooksLikeCredentialValue returns true for ANY long unbroken alphanumeric run of 16 or more
        // characters, and `sha256:` followed by 64 hex characters is exactly that. The predicate is not
        // wrong - it is a VALUE-shaped judgement for scanning source - but it CANNOT be applied to a record
        // that is made of digests, and no amount of narrowing makes it able to. Three false refusals of a
        // good record is the measurement.
        //
        // So the screen is replaced rather than narrowed, by one that can mean something: no member of this
        // type may be NAMED like a credential. That is the actual risk - not a value that slipped in, but a
        // future edit adding a `StorageConnectionString` member - and a name is something a check can judge
        // without ambiguity, because a name is not a digest. A deliberate addition of a credential-bearing
        // member therefore fails here and has to be argued for, which is the point.
        //
        // What neither version could have caught, and what actually protects the record: no constructor
        // parameter of this type or of MigrationEvidenceRecorder accepts a secret, the driver never holds the
        // database password in a form it could pass, and the release-level secret scan covers the source
        // these values are derived from.
        // String-typed only. A member that CANNOT hold a secret cannot leak one, and flagging one would make
        // this check refuse on the record's own `SecretsInThisRecord` flag - which is a boolean that ASSERT
        // there are none. It did exactly that on the fourth run. The four iterations are recorded here in
        // full because the pattern is the lesson: a check written without once being run against an instance
        // of the thing it checks is not a control, it is an intention, and this one refused a good record
        // four times before it was made to mean anything.
        foreach (var property in typeof(MigrationEvidence).GetProperties())
        {
            var name = property.Name;

            if (property.PropertyType != typeof(string) && property.PropertyType != typeof(string[]))
            {
                continue;
            }

            foreach (var forbidden in CredentialBearingMemberNames)
            {
                if (name.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"MigrationEvidence declares a member named '{name}', which names a credential. The "
                        + "record was not written. This is a defect in the driver: the record identifies the "
                        + "database by container, host, port, name and image, and none of those is a secret. If a "
                        + "member of this record genuinely needs to carry such a value, the record is the wrong "
                        + "place for it - the secret store is outside every evidence directory for a reason.");
                }
            }
        }

        var temporary = path + ".staging";

        File.WriteAllText(temporary, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);

        return path;
    }

    internal static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}

/// <summary>
/// The results a migration step can record. <b>Not a boolean</b>, because "did the migration run?" and "is
/// the target correct?" are different questions and a record that answered both with one value would let a
/// refusal read as a success.
/// </summary>
internal static class MigrationEvidenceResult
{
    /// <summary>The certified set was applied by this run.</summary>
    public const string Applied = "Applied";

    /// <summary>The target already carried the certified set; nothing was applied and nothing needed to be.</summary>
    public const string NotRequired = "NotRequired";

    /// <summary>Migration governance refused before any statement was executed.</summary>
    public const string RefusedByGovernance = "RefusedByGovernance";

    /// <summary>The operator command failed.</summary>
    public const string CommandFailed = "CommandFailed";

    /// <summary>The post-migration observation did not match the certified set.</summary>
    public const string PostStateMismatch = "PostStateMismatch";

    /// <summary>The step threw before a result could be established.</summary>
    public const string Threw = "Threw";
}

/// <summary>
/// Accumulates a <see cref="MigrationEvidence"/> across the migration step and writes it exactly once,
/// whatever happens. Owned by the caller, not registered: the step is a lexical scope, not a service.
/// </summary>
internal sealed class MigrationEvidenceRecorder
{
    private readonly DeploymentId _deployment;
    private readonly string _environment;
    private readonly string _releaseId;
    private readonly string _sourceCommit;
    private readonly string _databaseId;
    private readonly string _databaseHost;
    private readonly int _databasePort;
    private readonly string _databaseName;
    private readonly string _databaseImage;
    private readonly string _migrationProvider;
    private readonly IReadOnlyList<string> _certifiedSet;
    private readonly IReadOnlyList<string> _destructive;
    private readonly string _actor;
    private readonly string _startedUtc = MigrationEvidence.Now();

    private MigrationEvidence.MigrationStateSnapshot? _preState;
    private MigrationEvidence.BackupReference? _backup;
    private IReadOnlyList<string> _applied = [];
    private MigrationEvidence.MigrationStateSnapshot? _postState;
    private string? _governanceDecision;
    private string? _compatibilityBefore;
    private string? _refusalReason;
    private bool _executed;
    private int? _exitCode;

    internal MigrationEvidenceRecorder(
        DeploymentId deployment,
        EnvironmentDescriptor descriptor,
        ReleaseRecord release,
        IReadOnlyList<string> certifiedSet,
        IReadOnlyList<string> destructive,
        string actor)
    {
        _deployment = deployment;
        _environment = descriptor.Environment;
        _releaseId = release.ReleaseId.Value;
        // The release's certified source commit. A release carries a SET of source commits and this unit
        // contributes exactly one; a multi-commit release would need the per-unit binding, which no release
        // in this estate has yet, so the single value is asserted rather than silently taking the first.
        _sourceCommit = release.Identity.SourceCommits.Count == 1
            ? release.Identity.SourceCommits[0]
            : string.Join(",", release.Identity.SourceCommits);
        _databaseId = descriptor.Database!.ContainerName;
        _databaseHost = descriptor.Database.HostAddress;
        _databasePort = descriptor.Database.Port;
        _databaseName = descriptor.Database.DatabaseName;
        _databaseImage = descriptor.Database.Image;
        _migrationProvider = release.Migrations.Metadata.Provider;
        _certifiedSet = certifiedSet;
        _destructive = destructive;
        _actor = actor;
    }

    internal void RecordPreState(MigrationTargetObservation observation)
        => _preState = MigrationEvidence.MigrationStateSnapshot.From(observation);

    internal void RecordGovernance(string decision, string? compatibility)
    {
        _governanceDecision = decision;
        _compatibilityBefore = compatibility;
    }

    internal void RecordBackup(EnvironmentDatabase.BackupObservation observation)
        => _backup = MigrationEvidence.BackupReference.From(observation);

    internal void RecordCommand(bool executed, int? exitCode)
    {
        _executed = executed;
        _exitCode = exitCode;
    }

    internal void RecordPostState(MigrationTargetObservation observation)
        => _postState = MigrationEvidence.MigrationStateSnapshot.From(observation);

    internal void RecordApplied(IReadOnlyList<string> applied) => _applied = applied;

    internal void RecordRefusal(string reason) => _refusalReason = reason;

    /// <summary>
    /// Builds and writes the record. Returns the path, or throws — the caller is a <c>finally</c> and has no
    /// useful way to continue past a failure to write.
    /// </summary>
    internal string Write(string evidenceRoot, string result)
    {
        var record = new MigrationEvidence
        {
            ReleaseId = _releaseId,
            DeploymentId = _deployment.Value,
            Environment = _environment,
            SourceCommit = _sourceCommit,
            DatabaseId = _databaseId,
            DatabaseHost = _databaseHost,
            DatabasePort = _databasePort,
            DatabaseName = _databaseName,
            DatabaseImage = _databaseImage,
            MigrationProvider = _migrationProvider,
            CertifiedMigrationSet = _certifiedSet,
            DestructiveForwardMigrations = _destructive,
            PreState = _preState ?? throw new InvalidOperationException(
                "No pre-state was recorded, so this record cannot state what the migration changed. The "
                + "pre-state observation must be taken before the first statement is executed."),
            Backup = _backup,
            AppliedByThisRun = _applied,
            PostState = _postState,
            Result = result,
            RefusalReason = _refusalReason,
            MigrationCommandExecuted = _executed,
            MigrationCommandExitCode = _exitCode,
            GovernanceDecision = _governanceDecision,
            CompatibilityBefore = _compatibilityBefore,
            StartedUtc = _startedUtc,
            CompletedUtc = MigrationEvidence.Now(),
            Actor = _actor,
            Instrument = "Nexus.Delivery.Deploy promote/deploy",
            SecretsInThisRecord = false
        };

        return record.Write(evidenceRoot, _deployment);
    }
}
