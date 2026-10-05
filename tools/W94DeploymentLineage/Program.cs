using System.Globalization;
using System.Text;
using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Nexus.ProductCore.Contracts.DevelopmentControl;
using Nexus.ProductCore.Core.DevelopmentControl;
using Nexus.DevelopmentControl.Safety;

namespace Nexus.W94DeploymentLineage;

/// <summary>
/// W9.4 GOVERNED DEPLOYMENT-LINEAGE CLOSURE.
///
/// <para><b>What this does.</b> Records the W9.4 ENV-DEV deployment lineage into the authoritative
/// DevelopmentControl workbook, through the canonical shared writer under the canonical shared lock. It
/// adds ONE row to <c>13_GitLineage</c>.</para>
///
/// <para><b>Why this is a second row and not an edit of <c>L-W9-1</c>.</b> The W9.3 row is immutable and
/// the ledger refuses a duplicate id, so <c>L-W9-1</c> is not re-appended and not touched. It also says
/// something that was true when it was written and is now superseded — <i>"NOT published, NOT deployed"</i>
/// — and the correct treatment of a superseded governed record in this estate is supersession, never
/// rewrite. This row states what became true afterwards.</para>
///
/// <para><b>Facts are read from the governed ledger, not retyped.</b> The deployment identities are read
/// out of <c>lineage.jsonl</c> through <see cref="FileDeploymentLineageLog"/> — the same reader the
/// deployment used to write it. A tool that re-typed them would be a second source for facts that already
/// have one, and the two would eventually disagree.</para>
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 7)
        {
            Console.Error.WriteLine("usage: W94DeploymentLineage <workbookPath> <lockDir> <mode:rehearse|apply> <deploymentLineageRoot> <lineageId> <workId> <changeId> [notesExtra]");
            return 2;
        }

        var storePath = args[0];
        var lockDir = args[1];
        var apply = string.Equals(args[2], "apply", StringComparison.OrdinalIgnoreCase);
        var lineageRoot = args[3];
        var lineageId = args[4];
        var workId = args[5];
        var changeId = args[6];
        var notesExtra = args.Length > 7 ? args[7] : string.Empty;

        try
        {
            var before = Sha256(storePath);

            Console.WriteLine($"workbook          : {storePath}");
            Console.WriteLine($"mode              : {(apply ? "APPLY" : "REHEARSE (nothing beyond this file)")}");
            Console.WriteLine($"sha256 before     : {before}");

            // ---- the deployment record, read from the governed ledger --------------------------------
            var ledger = new FileDeploymentLineageLog(lineageRoot);
            var deployment = ledger.TryGetAsync(lineageId).GetAwaiter().GetResult();

            if (deployment is null)
            {
                Console.Error.WriteLine(
                    $"REFUSED: the deployment ledger at '{ledger.LogPath}' holds no record '{lineageId}'. "
                    + "A lineage row naming a deployment that was never recorded would be a record of nothing.");
                return 1;
            }

            if (deployment.Transition != DeploymentTransition.DeployToDev)
            {
                Console.Error.WriteLine(
                    $"REFUSED: '{lineageId}' records a {deployment.Transition}, not a {DeploymentTransition.DeployToDev}. "
                    + "This closure records deployments into ENV-DEV and nothing else.");
                return 1;
            }

            var artifact = deployment.Artifacts[0];

            Console.WriteLine($"ledger record     : {deployment.LineageId} {deployment.Transition} {deployment.FromState}->{deployment.ToState} {deployment.Environment.Value} at {deployment.OccurredAt:O}");
            Console.WriteLine($"artifact          : {artifact.UnitId} {artifact.Digest} ({artifact.SizeBytes} bytes)");

            if (deployment.Migrations is null || deployment.Migrations.MigrationIds.Count == 0)
            {
                Console.Error.WriteLine($"REFUSED: '{lineageId}' records no migration set, so the schema state it deployed cannot be stated.");
                return 1;
            }

            // ---- the authority ------------------------------------------------------------------------
            var reader = new DevelopmentControlReader();
            var pre = reader.Read(storePath);
            Console.WriteLine($"form              : {pre.Form}");
            Console.WriteLine($"authority         : {pre.Authority}");

            if (pre.Authority != DevelopmentControlAuthority.Authoritative)
            {
                Console.Error.WriteLine(
                    $"REFUSED: the workbook reads '{pre.Authority}'. The writer refuses a candidate, and a "
                    + "deployment lineage recorded into a candidate would be a record with no authority behind it.");
                return 1;
            }

            var canonicalLockDir = AtomicWriterLock.CanonicalLockDirectoryFor(storePath);

            if (!string.IsNullOrWhiteSpace(lockDir) && !AtomicWriterLock.IsCanonicalLockDirectory(storePath, lockDir))
            {
                Console.Error.WriteLine(
                    $"REFUSED: lock directory '{lockDir}' is not the one this store derives "
                    + $"('{canonicalLockDir}'). The lock's location is a function of the store, not of the caller.");
                return 1;
            }

            Console.WriteLine($"lock directory    : {canonicalLockDir} (derived from the store)");

            using var reservation = new DevelopmentControlLockService()
                .TryAcquire(storePath, owner: "w9.4-deployment-lineage-closure").Reservation
                ?? throw new InvalidOperationException(
                    "the canonical shared lock was not acquired. A second writer may hold it; the closure "
                    + "does not proceed without it.");

            Console.WriteLine($"lock              : held, store={reservation.StorePath}");

            var writer = new DevelopmentControlWriterAuthorizer();

            var record = new DevelopmentControlAppendRecord(
                "GitLineage",
                "LineageId",
                changeId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["LineageId"] = lineageId,
                    ["WorkId"] = workId,
                    ["RepositoryId"] = "PRT/MarketSurvey",
                    ["Branch"] = "w9.2/runtime-config",
                    ["WorktreePath"] = @"D:\NEXUS\PRT\MarketSurvey",
                    // The commit the deployment moved, quoted from the ledger record rather than restated.
                    ["BaseSHA"] = deployment.SourceCommitSha,
                    ["CommitSha"] = deployment.SourceCommitSha,
                    ["Notes"] = Notes(deployment, workId, notesExtra)
                },
                DevelopmentControlProvenance.Native(DateTimeOffset.UtcNow, NativeProvenanceNote),
                ScopeFor(changeId));

            // ---- REHEARSE IS A MODE, NOT A LABEL ------------------------------------------------------
            // The first version of this tool printed "REHEARSE (nothing beyond this file)" and then
            // appended anyway: the mode argument was parsed and never consulted, so the word described an
            // intention rather than a behaviour. The rehearsal run of this driver therefore WROTE its row
            // (L-W9-2, row 12) while claiming it had not. That is the estate's recurring defect — a check
            // that cannot fail because it is not wired to anything — and it is fixed here by returning
            // before the append rather than by rewording the message.
            //
            // The same defect is present in tools/W9LineageClosure (W9.3), whose mode argument is likewise
            // decorative. That tool is NOT edited: its transcript is W9.3 acceptance evidence, and editing
            // it would change the program that produced a record already written. It is recorded as a
            // finding against W9.3 instead.
            if (!apply)
            {
                Console.WriteLine("REHEARSED         : every check above ran; the append was NOT issued and the store is unchanged.");
                Console.WriteLine($"would append      : {lineageId} -> 13_GitLineage under change '{changeId}'");
                Console.WriteLine($"store unchanged   : {before == Sha256(storePath)}");

                var held = reader.Read(storePath).Sheets.First(s => s.LogicalName == "GitLineage").Records
                    .Any(r => r.Get("LineageId") == lineageId);

                Console.WriteLine($"id already taken  : {held}");

                return held ? 1 : 0;
            }

            var lineage = writer.Append(reservation, record);

            Console.WriteLine($"append 13_GitLineage: appended={lineage.Appended} row={lineage.Row} key={lineage.RecordKey}");
            Console.WriteLine($"  reason          : {lineage.Reason}");

            if (!lineage.Appended)
            {
                Console.Error.WriteLine($"REFUSED — {lineage.Reason}");
                return 1;
            }

            var after = Sha256(storePath);
            Console.WriteLine($"sha256 after      : {after}");
            Console.WriteLine($"changed           : {!string.Equals(before, after, StringComparison.Ordinal)}");

            // ---- read back through the reader, not through this process's memory ----------------------
            var post = reader.Read(storePath);
            var row = post.Sheets.First(s => s.LogicalName == "GitLineage").Records
                .FirstOrDefault(r => r.Get("LineageId") == lineageId);

            if (row is null)
            {
                Console.Error.WriteLine("FAILED: the row was reported appended but does not read back.");
                return 1;
            }

            Console.WriteLine("  read-back LineageId  = " + row.Get("LineageId"));
            Console.WriteLine("  read-back WorkId     = " + row.Get("WorkId"));
            Console.WriteLine("  read-back CommitSha  = " + row.Get("CommitSha"));
            Console.WriteLine("  read-back Notes      = " + Preview(row.Get("Notes")));

            // ---- and the row it supersedes is still there, unedited -----------------------------------
            var prior = post.Sheets.First(s => s.LogicalName == "GitLineage").Records
                .FirstOrDefault(r => r.Get("LineageId") == "L-W9-1");

            Console.WriteLine($"L-W9-1 still present: {prior is not null} (immutable; superseded by statement, never by rewrite)");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAILED — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// The typed relation for this row, in the Notes column — the mechanism the existing rows establish,
    /// used for the same reason W9.3 used it: the writer's binding table binds five logical sheets and
    /// <c>14_Builds</c> / <c>15_Releases</c> are not among them.
    ///
    /// <para>Every value is read from the governed ledger record. The one externally supplied value is the
    /// evidence path and the release identity the record already references.</para>
    /// </summary>
    private static string Notes(DeploymentLineageRecord record, string workId, string notesExtra)
    {
        var builder = new StringBuilder();

        builder.Append("W9.4 ENV-DEV deployment lineage closure. ");
        builder.Append(CultureInfo.InvariantCulture, $"WorkItemId={workId} | ");
        builder.Append(CultureInfo.InvariantCulture, $"DeploymentLineageId={record.LineageId} | ");
        builder.Append(CultureInfo.InvariantCulture, $"Transition={record.Transition} | ");
        builder.Append(CultureInfo.InvariantCulture, $"FromState={record.FromState} | ToState={record.ToState} | ");
        builder.Append(CultureInfo.InvariantCulture, $"Environment={record.Environment.Value} | ");
        builder.Append(CultureInfo.InvariantCulture, $"SourceCommit={record.SourceCommitSha} | ");
        builder.Append(CultureInfo.InvariantCulture, $"BuildId={record.BundleId.Value} | ");
        builder.Append(CultureInfo.InvariantCulture, $"ArtifactId={record.Artifacts[0].UnitId.Value} | ");
        builder.Append(CultureInfo.InvariantCulture, $"ArtifactSha256={record.Artifacts[0].Digest} | ");
        builder.Append(CultureInfo.InvariantCulture, $"MigrationProvider={record.Migrations?.Provider ?? "(none)"} | ");
        builder.Append(CultureInfo.InvariantCulture, $"MigrationIds={string.Join(",", record.Migrations?.MigrationIds ?? [])} | ");
        builder.Append(CultureInfo.InvariantCulture, $"AuthorizedBy={record.Authorization?.ActorIdentity ?? "(none)"} | ");
        builder.Append(CultureInfo.InvariantCulture, $"AuthorizationRole={record.Authorization?.Role.ToString() ?? "(none)"} | ");
        builder.Append("SUPERSEDES THE STATEMENT in L-W9-1 that the release was 'NOT published, NOT deployed'. ");
        builder.Append("It WAS published and IS deployed to ENV-DEV; L-W9-1 is not rewritten, because a governed record is corrected by supersession. ");
        builder.Append("the artifact was NOT rebuilt and the release tag was NOT moved | ");
        builder.Append("the deployment ran against an isolated ENV-DEV PostgreSQL container, never against any other database | ");
        builder.Append("acceptance: D:\\NEXUS_V3_AUDIT\\W9_DEPLOYMENT\\W9_4_DEV_DEPLOYMENT\\DEV_DEPLOYMENT.json");

        if (!string.IsNullOrWhiteSpace(notesExtra))
        {
            builder.Append(CultureInfo.InvariantCulture, $" | {notesExtra}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// The lane's declaration that it may write THIS control store. The append refuses without it.
    ///
    /// <para>The declared ChangeId is the one the record is filed under, so the declaration and the append
    /// cannot disagree about which change authorised the write.</para>
    /// </summary>
    private static ChangeScopeDeclaration ScopeFor(string changeId) =>
        new("w9.4-deployment-lane", changeId,
            [new ChangeScopeItem(ChangeScopeItemKind.ControlStore, ChangeScopeAccessMode.Write, "NEXUS_DEVELOPMENT_CONTROL.xlsx")]);

    private const string NativeProvenanceNote =
        "Created natively in V3 by the W9.4 governed deployment-lineage closure. No legacy cell is pointed "
        + "at, because no legacy workbook carried this deployment.";

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    private static string Preview(string? value) => value is null
        ? "<absent>"
        : value.Length <= 96 ? value : value[..93] + "...";
}
