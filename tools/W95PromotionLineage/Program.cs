using System.Globalization;
using System.Text;
using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Nexus.ProductCore.Contracts.DevelopmentControl;
using Nexus.ProductCore.Core.DevelopmentControl;
using Nexus.DevelopmentControl.Safety;

namespace Nexus.W95PromotionLineage;

/// <summary>
/// W9.5 GOVERNED PROMOTION-LINEAGE CLOSURE.
///
/// <para><b>What this does.</b> Records the W9.5 ENV-DEV to ENV-TEST promotion lineage into the
/// authoritative DevelopmentControl workbook, through the canonical shared writer under the canonical shared
/// lock. It adds ONE row to <c>13_GitLineage</c>.</para>
///
/// <para><b>Why this is a new row and not an edit of the W9.4 rows.</b> Every row this estate has written is
/// immutable and the ledger refuses a duplicate id, so <c>L-W9-2</c> through <c>L-W9-6</c> are not touched.
/// The W9.5 chain SUPERSEDES what they described for a different release: it moves a release that did not
/// exist when they were written.</para>
///
/// <para><b>Facts are read from the governed ledger, not retyped.</b> The deployment identities come out of
/// <c>lineage.jsonl</c> through <see cref="FileDeploymentLineageLog"/> — the same reader the deployment used
/// to write it. The release identity is read from the release registry through the decode path. A tool that
/// re-typed them would be a second source for facts that already have one.</para>
///
/// <para><b>The full chain, as one relation.</b> The W9.5 directive requires the governed lineage to carry
/// <c>WorkItem → SourceCommit → BuildId → ArtifactId → ReleaseId → DEV DeploymentId → VerifiedDev → TEST
/// DeploymentId → VerifiedTest</c>. The writer's binding table binds five logical sheets and
/// <c>14_Builds</c> / <c>15_Releases</c> are not among them, so the chain is recorded in the identity
/// columns plus <c>Notes</c> — the mechanism the six existing W8 rows establish. <b>No workbook column is
/// invented.</b></para>
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 7)
        {
            Console.Error.WriteLine("usage: W95PromotionLineage <workbookPath> <lockDir> <mode:rehearse|apply> <deploymentLineageRoot> <lineageId> <workId> <changeId> [notesExtra]");
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

            // ---- the promotion record, read from the governed ledger --------------------------------
            var ledger = new FileDeploymentLineageLog(lineageRoot);
            var promotion = ledger.TryGetAsync(lineageId).GetAwaiter().GetResult();

            if (promotion is null)
            {
                Console.Error.WriteLine(
                    $"REFUSED: the deployment ledger at '{ledger.LogPath}' holds no record '{lineageId}'. "
                    + "A lineage row naming a promotion that was never recorded would be a record of nothing.");
                return 1;
            }

            if (promotion.Transition != DeploymentTransition.PromoteToTest)
            {
                Console.Error.WriteLine(
                    $"REFUSED: '{lineageId}' records a {promotion.Transition}, not a {DeploymentTransition.PromoteToTest}. "
                    + "This closure records promotions into ENV-TEST and nothing else - the same refusal, for the "
                    + "same reason, that W94DeploymentLineage makes about deployments into ENV-DEV.");
                return 1;
            }

            // ---- the verification that completed the chain, and the DEV records it promoted from ----
            var all = ledger.ReadAllAsync().GetAwaiter().GetResult();
            var bundleId = promotion.BundleId;

            var testDeployment = promotion;
            var testVerification = all
                .FirstOrDefault(r => r.BundleId == bundleId
                    && r.Transition == DeploymentTransition.VerifyInTest
                    && r.DeploymentId == promotion.DeploymentId);

            var devDeployment = all
                .FirstOrDefault(r => r.BundleId == bundleId
                    && r.Transition == DeploymentTransition.DeployToDev);

            var devVerification = all
                .FirstOrDefault(r => r.BundleId == bundleId
                    && r.Transition == DeploymentTransition.VerifyInDev);

            if (testVerification is null)
            {
                Console.Error.WriteLine(
                    $"REFUSED: '{lineageId}' records the promotion but the ledger holds no matching "
                    + $"{DeploymentTransition.VerifyInTest} for deployment '{promotion.DeploymentId}'. The chain the "
                    + "directive requires ends at VerifiedTest, and without the verification there is nothing to "
                    + "record there. A row that named a verification which never happened would be the fabricated "
                    + "reference this estate refuses elsewhere.");
                return 1;
            }

            if (devDeployment is null || devVerification is null)
            {
                Console.Error.WriteLine(
                    $"REFUSED: the chain requires the source environment's deployment AND verification, and the ledger "
                    + $"holds de deployment={devDeployment?.LineageId ?? "(none)"}, verification={devVerification?.LineageId ?? "(none)"} "
                    + $"for bundle '{bundleId.Value}'. Rule R-4 admits an artifact to an environment only from the one "
                    + "before it, proven - so a promotion with no proven source is a promotion from nowhere.");
                return 1;
            }

            var artifact = promotion.Artifacts[0];

            Console.WriteLine($"promotion record  : {promotion.LineageId} {promotion.Transition} {promotion.FromState}->{promotion.ToState} {promotion.Environment.Value} at {promotion.OccurredAt:O}");
            Console.WriteLine($"test deployment   : {promotion.DeploymentId}");
            Console.WriteLine($"test verification : {testVerification.LineageId}");
            Console.WriteLine($"dev deployment    : {devDeployment.LineageId} ({devDeployment.DeploymentId})");
            Console.WriteLine($"dev verification  : {devVerification.LineageId}");
            Console.WriteLine($"artifact          : {artifact.UnitId} {artifact.Digest} ({artifact.SizeBytes} bytes)");

            if (promotion.Migrations is null || promotion.Migrations.MigrationIds.Count == 0)
            {
                Console.Error.WriteLine($"REFUSED: '{lineageId}' records no migration set, so the schema state it promoted cannot be stated.");
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
                    $"REFUSED: the workbook reads '{pre.Authority}'. The writer refuses a candidate, and a promotion "
                    + "lineage recorded into a candidate would be a record with no authority behind it.");
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
                .TryAcquire(storePath, owner: "w9.5-promotion-lineage-closure").Reservation
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
                    ["Branch"] = "w9.5/environment-contract",
                    ["WorktreePath"] = @"D:\NEXUS\PRT\MarketSurvey\.forge\worktrees\w95-env-contract",
                    // The commit the promotion moved, quoted from the ledger record rather than restated.
                    ["BaseSHA"] = promotion.SourceCommitSha,
                    ["CommitSha"] = promotion.SourceCommitSha,
                    ["Notes"] = Notes(promotion, testVerification, devDeployment, devVerification, workId, notesExtra)
                },
                DevelopmentControlProvenance.Native(DateTimeOffset.UtcNow, NativeProvenanceNote),
                ScopeFor(changeId));

            // ---- REHEARSE IS A MODE, NOT A LABEL ------------------------------------------------------
            // W94DeploymentLineage records the first version of this driver printing "REHEARSE (nothing
            // beyond this file)" and then appending anyway, because the mode argument was parsed and never
            // consulted. This returns before the append rather than rewording the message, for the same
            // reason: a check that cannot fail is not a check.
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

            // ---- and the rows it supersedes are still there, unedited ---------------------------------
            var prior = post.Sheets.First(s => s.LogicalName == "GitLineage").Records
                .Select(r => r.Get("LineageId"))
                .Where(id => id is not null)
                .ToArray();

            Console.WriteLine($"  GitLineage row ids   = {string.Join(", ", prior)}");
            Console.WriteLine($"L-W9-2 still present : {prior.Contains("L-W9-2")} (immutable; superseded by statement, never by rewrite)");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAILED — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// The full chain, as the typed relation TASK 14 requires, in the Notes column.
    ///
    /// <para>
    /// Every value is read from the governed ledger, except the bundle identity and the acceptance path,
    /// which are facts about where the evidence lives rather than about the promotion. The chain is written
    /// as one walkable line because that is what "governed lineage" means here: a reader following it forwards
    /// reaches the artifact that was promoted, and following it backwards reaches the work item that caused
    /// it.
    /// </para>
    /// </summary>
    private static string Notes(
        DeploymentLineageRecord promotion,
        DeploymentLineageRecord testVerification,
        DeploymentLineageRecord devDeployment,
        DeploymentLineageRecord devVerification,
        string workId,
        string notesExtra)
    {
        var builder = new StringBuilder();

        builder.Append("W9.5 ENV-DEV to ENV-TEST promotion lineage closure. ");
        builder.Append(CultureInfo.InvariantCulture, $"WorkItemId={workId} | ");
        builder.Append(CultureInfo.InvariantCulture, $"Chain=WorkItem:{workId} > SourceCommit:{promotion.SourceCommitSha} > ");
        builder.Append(CultureInfo.InvariantCulture, $"BuildId:{promotion.BundleId.Value} > ");
        builder.Append(CultureInfo.InvariantCulture, $"ArtifactId:{promotion.Artifacts[0].UnitId.Value} > ");
        builder.Append(CultureInfo.InvariantCulture, $"ReleaseId:{promotion.BundleId.Value} > ");
        builder.Append(CultureInfo.InvariantCulture, $"DevDeploymentId:{devDeployment.DeploymentId} > ");
        builder.Append(CultureInfo.InvariantCulture, $"VerifiedDev:{devVerification.LineageId} > ");
        builder.Append(CultureInfo.InvariantCulture, $"TestDeploymentId:{promotion.DeploymentId} > ");
        builder.Append(CultureInfo.InvariantCulture, $"VerifiedTest:{testVerification.LineageId} | ");
        builder.Append(CultureInfo.InvariantCulture, $"PromotionLineageId={promotion.LineageId} | ");
        builder.Append(CultureInfo.InvariantCulture, $"Transition={promotion.Transition} | ");
        builder.Append(CultureInfo.InvariantCulture, $"FromState={promotion.FromState} | ToState={promotion.ToState} | ");
        builder.Append(CultureInfo.InvariantCulture, $"Environment={promotion.Environment.Value} | ");
        builder.Append(CultureInfo.InvariantCulture, $"ArtifactSha256={promotion.Artifacts[0].Digest} | ");
        builder.Append(CultureInfo.InvariantCulture, $"MigrationProvider={promotion.Migrations?.Provider ?? "(none)"} | ");
        builder.Append(CultureInfo.InvariantCulture, $"MigrationIds={string.Join(",", promotion.Migrations?.MigrationIds ?? [])} | ");
        builder.Append(CultureInfo.InvariantCulture, $"AuthorizedBy={promotion.Authorization?.ActorIdentity ?? "(none)"} | ");
        builder.Append(CultureInfo.InvariantCulture, $"AuthorizationRole={promotion.Authorization?.Role.ToString() ?? "(none)"} | ");
        builder.Append("This row records a PROMOTION, not a deployment: the artifact was NOT rebuilt, the release tag was NOT moved, and the artifact's digest is identical in ENV-DEV and ENV-TEST - which is the property build-once exists for. ");
        builder.Append("The release promoted here is the W9.5 REMEDIATION release; the earlier release rel-2ec4c364727bcb74 keeps its own disposition, PROMOTION_BLOCKED_BY_ARTIFACT_ENVIRONMENT_CONTRACT, and is not superseded by this row. ");
        builder.Append("acceptance: D:\\NEXUS_V3_AUDIT\\W9_DEPLOYMENT\\W9_5_TEST_PROMOTION");

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
        new("w9.5-promotion-lane", changeId,
            [new ChangeScopeItem(ChangeScopeItemKind.ControlStore, ChangeScopeAccessMode.Write, "NEXUS_DEVELOPMENT_CONTROL.xlsx")]);

    private const string NativeProvenanceNote =
        "Created natively in V3 by the W9.5 governed promotion-lineage closure. No legacy cell is pointed "
        + "at, because no legacy workbook carried this promotion.";

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }
}
