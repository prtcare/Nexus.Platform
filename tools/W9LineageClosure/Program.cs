using Nexus.DevelopmentControl.Safety;
using Nexus.ProductCore.Contracts.DevelopmentControl;
using Nexus.ProductCore.Core.DevelopmentControl;

namespace Nexus.W9LineageClosure;

/// <summary>
/// W9.3 GOVERNED LINEAGE CLOSURE.
///
/// <para><b>What this does.</b> Records the W9.3 release lineage into the authoritative
/// DevelopmentControl workbook, through the canonical shared writer under the canonical shared lock.
/// It adds ONE row to <c>13_GitLineage</c>: the relationship
/// <c>WorkItem → SourceCommit → BuildId → ArtifactId → ReleaseId</c>.</para>
///
/// <para><b>Why the chain lives in the identity columns plus Notes, and not in new columns.</b> The
/// V3 schema HAS <c>14_Builds</c> and <c>15_Releases</c> sheets, and this was checked rather than
/// assumed — but the canonical writer's binding table binds five logical sheets
/// (<c>Dependencies</c>, <c>GitLineage</c>, <c>ChangeScopes</c>, <c>ChangeRequests</c>,
/// <c>WorkGraph</c>), and <c>Builds</c> and <c>Releases</c> are not among them. An append targeting
/// either would be refused, correctly: a writer that could write to any sheet it found would be one
/// whose declared bindings meant nothing.</para>
///
/// <para>So the chain is recorded the way W8 recorded its own: the two links the sheet has columns
/// for (<c>WorkId</c> and <c>CommitSha</c>) go in those columns, and the rest of the typed relation
/// goes in <c>Notes</c> — the mechanism the six existing rows establish. Nothing is invented: every
/// key written is one a reader can resolve against the certified release evidence.</para>
///
/// <para><b>Nothing here derives an identity.</b> Every value is read from the W9.2/W9.3 evidence or
/// the live registry. The artifact is not rebuilt, the release tag is not touched, and no release
/// state is altered.</para>
/// </summary>
internal static class Program
{
    // ---------------------------------------------------------------- the recorded identities
    //
    // Read from D:\NEXUS_V3_AUDIT\W9_DEPLOYMENT\W9_3_RELEASE_BUNDLE and the live registry.
    // VERIFIED, not derived: each of these is quoted from an artifact that already existed.

    private const string LineageId = "L-W9-1";
    private const string WorkId = "WI-09-3.1";
    private const string ChangeId = "CHG-W9-001";

    private const string RepositoryId = "PRT/MarketSurvey";
    private const string Branch = "w9.2/runtime-config";
    private const string WorktreePath = @"D:\NEXUS\PRT\MarketSurvey";
    private const string BaseSha = "962502323db521605e16226ffeeb39a1231cd8c9";
    private const string CommitSha = "2f6f93096067f7d13705d6259b9b982a0bb59f79";

    private const string BuildId = "bld-9b8996cf89308d68";
    private const string ArtifactId = "marketsurvey.api/dotnet-app/marketsurvey.api@0.1.0";
    private const string ArtifactSha256 = "sha256:0ab87652cb33dea1787e1255a67880abc288548daf52c4bccaf30a05b4af4a7e";
    private const string ReleaseId = "rel-2ec4c364727bcb74";
    private const string ReleaseRefName = "refs/tags/release/marketsurvey.api/0.1.0";
    private const string RecordDigest = "sha256:d2c33e8e00898fb7e5d8eecf7a099dcb9f3c72ed5c5edfc41d1c06542fd0317f";

    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: W9LineageClosure <workbookPath> <lockDir> <mode:rehearse|apply>");
            return 2;
        }

        var storePath = args[0];
        var lockDir = args[1];
        var apply = string.Equals(args[2], "apply", StringComparison.OrdinalIgnoreCase);

        try
        {
            var before = Sha256(storePath);

            Console.WriteLine($"workbook          : {storePath}");
            Console.WriteLine($"mode              : {(apply ? "APPLY" : "REHEARSE (nothing beyond this file)")}");
            Console.WriteLine($"sha256 before     : {before}");

            var reader = new DevelopmentControlReader();
            var pre = reader.Read(storePath);
            Console.WriteLine($"form              : {pre.Form}");
            Console.WriteLine($"authority         : {pre.Authority}");

            if (pre.Authority != DevelopmentControlAuthority.Authoritative)
            {
                Console.Error.WriteLine(
                    $"REFUSED: the workbook reads '{pre.Authority}'. The writer refuses a candidate, and a "
                    + "release lineage recorded into a candidate would be a record with no authority behind it.");
                return 1;
            }

            // The lock's LOCATION is derived from the store, not named by this caller. The directory
            // argument is still accepted so the invocation recorded in the W9.3 evidence runs unchanged,
            // but it is validated rather than honoured: a caller-chosen location is how two writers came
            // to hold two files for one identity.
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
                .TryAcquire(storePath, canonicalLockDir, "w9.3-governed-lineage-closure").Reservation
                ?? throw new InvalidOperationException(
                    "the canonical shared lock was not acquired. A second writer may hold it; the closure "
                    + "does not proceed without it.");

            Console.WriteLine($"lock              : held, store={reservation.StorePath}");

            var writer = new DevelopmentControlWriterAuthorizer();

            // ---- the lineage relation ------------------------------------------------------------
            var lineage = writer.Append(reservation, new DevelopmentControlAppendRecord(
                "GitLineage",
                "LineageId",
                ChangeId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["LineageId"] = LineageId,
                    ["WorkId"] = WorkId,
                    ["RepositoryId"] = RepositoryId,
                    ["Branch"] = Branch,
                    ["WorktreePath"] = WorktreePath,
                    ["BaseSHA"] = BaseSha,
                    ["CommitSha"] = CommitSha,
                    ["Notes"] = Notes,
                },
                DevelopmentControlProvenance.Native(DateTimeOffset.UtcNow, NativeProvenanceNote),
                ScopeFor(ChangeId)));

            Report("13_GitLineage", lineage);

            if (!lineage.Appended)
            {
                Console.Error.WriteLine($"REFUSED — {lineage.Reason}");
                return 1;
            }

            var after = Sha256(storePath);
            Console.WriteLine($"sha256 after      : {after}");
            Console.WriteLine($"changed           : {!string.Equals(before, after, StringComparison.Ordinal)}");
            Console.WriteLine($"lineage row       : {lineage.Row}  key={lineage.RecordKey}");
            Console.WriteLine($"containment       : {lineage.Containment}");

            // ---- read back through the reader, not through this process's memory -------------------
            var post = reader.Read(storePath);
            var row = post.Sheets.First(s => s.LogicalName == "GitLineage").Records
                .FirstOrDefault(r => r.Get("LineageId") == LineageId);

            if (row is null)
            {
                Console.Error.WriteLine("FAILED: the row was reported appended but does not read back.");
                return 1;
            }

            Console.WriteLine("  read-back LineageId  = " + row.Get("LineageId"));
            Console.WriteLine("  read-back WorkId     = " + row.Get("WorkId"));
            Console.WriteLine("  read-back CommitSha  = " + row.Get("CommitSha"));
            Console.WriteLine("  read-back Notes      = " + Preview(row.Get("Notes")));

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAILED — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void Report(string sheet, DevelopmentControlAppendResult result)
    {
        Console.WriteLine($"append {sheet,-16}: appended={result.Appended} row={result.Row} key={result.RecordKey}");
        Console.WriteLine($"  reason          : {result.Reason}");
    }

    /// <summary>
    /// The lane's declaration that it may write THIS control store.
    ///
    /// <para>The append refuses without it: a missing declaration, a declaration with no ControlStore
    /// item, a ControlStore item declared READ, and one naming a different store are four refusals
    /// that all resolve to "amend your scope". Read is called out separately on purpose — declaring
    /// that you will <i>read</i> the authority is not a claim to write it.</para>
    /// </summary>
    private static ChangeScopeDeclaration ScopeFor(string changeId) =>
        new("w9.3-release-lane", changeId,
            [new ChangeScopeItem(ChangeScopeItemKind.ControlStore, ChangeScopeAccessMode.Write, "NEXUS_DEVELOPMENT_CONTROL.xlsx")]);

    private const string NativeProvenanceNote =
        "Created natively in V3 by the W9.3 governed lineage closure. No legacy cell is pointed at, "
        + "because no legacy workbook carried this release.";

    /// <summary>
    /// The typed relation, in the Notes column.
    ///
    /// <para><b>Why the whole chain goes here rather than being split across 14_Builds and
    /// 15_Releases.</b> Those sheets exist but are not in the writer's binding table, so an append to
    /// them is refused. TASK 2 authorises exactly this fallback — "use the approved typed relation /
    /// Notes mechanism already established by W8" — and the six existing GitLineage rows establish it.
    /// Every key below resolves to a value that already existed before this closure ran.</para>
    /// </summary>
    private const string Notes =
        "W9.3 governed release lineage closure. " +
        "WorkItemId=WI-09-3.1 | " +
        "SourceCommit=2f6f93096067f7d13705d6259b9b982a0bb59f79 | " +
        "BuildId=bld-9b8996cf89308d68 | " +
        "ArtifactId=marketsurvey.api/dotnet-app/marketsurvey.api@0.1.0 | " +
        "ArtifactSha256=sha256:0ab87652cb33dea1787e1255a67880abc288548daf52c4bccaf30a05b4af4a7e | " +
        "ReleaseId=rel-2ec4c364727bcb74 | " +
        "ReleaseRefName=refs/tags/release/marketsurvey.api/0.1.0 | " +
        "ReleaseRecordDigest=sha256:d2c33e8e00898fb7e5d8eecf7a099dcb9f3c72ed5c5edfc41d1c06542fd0317f | " +
        "release lifecycle=ReadyForDevPendingSecurityAction (credential rotation and remote release-tag " +
        "protection unconfirmed; NOT published, NOT deployed) | " +
        "buildId/artifactId/sha256 quoted from W9.2 BUILD_MANIFEST.txt and the W9.3 release registry; " +
        "the artifact was NOT rebuilt and the release tag was NOT touched | " +
        "tests: W9.3 Platform 410/0 (Delivery 342, Governance 36, Architecture 16, Scope 16) | " +
        "acceptance: D:\\NEXUS_V3_AUDIT\\W9_DEPLOYMENT\\W9_3_RELEASE_BUNDLE\\W9_3_REPORT.md | " +
        "rollback: the W9.3 commits 7a1b7f5,6193f7d,9e1ae14 are revertible from BaseSHA ca8a38c on " +
        "branch w9.3/release-bundle; nothing was pushed; the release tag is a local ref";

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    private static string Preview(string? value) => value is null
        ? "<absent>"
        : value.Length <= 96 ? value : value[..93] + "...";
}
