using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexus.DevelopmentControl.Safety;
using Nexus.ProductCore.Contracts.ReadModel;

namespace Nexus.ProductCore.Core.ReadModel;

/// <summary>
/// W10.4 TASK 6 — <b>reads DevelopmentControl authority and projects it onto the published read
/// contract.</b>
///
/// <para>
/// <b>Owner side, and read-only.</b> This type lives beside the canonical reader and calls it. It
/// opens the workbook for reading, takes no lock, acquires no mutation authority, and writes nothing.
/// A publisher that took the writer lock to export data would be claiming the right to change the
/// authority in order to describe it.
/// </para>
///
/// <para>
/// <b>It does not re-implement the reader.</b> <see cref="WorkbookCompatibilityReader.Read"/> already
/// resolves physical sheets to logical names and physical columns to logical columns, through the
/// same binding table the writer uses. Reading the OOXML here would be a second reader, and a second
/// reader is a second chance to disagree with the first — silently, because a workbook read two ways
/// still produces a plausible answer.
/// </para>
///
/// <para>
/// <b>Nothing is invented.</b> A record the authority does not hold is absent; an edge it does not
/// assert is a gap. This is the same rule W10.1 established for Delivery, applied to the second owner.
/// </para>
/// </summary>
public sealed class DevelopmentControlReadProjection
{
    private readonly string _workbookPath;

    private static readonly JsonSerializerOptions CanonicalOptions = new() { WriteIndented = false };

    public DevelopmentControlReadProjection(string workbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        _workbookPath = workbookPath;
    }

    /// <summary>
    /// Builds the payload from authority state. Pure with respect to the workbook: reads only, takes
    /// no clock, so a projection is reproducible for a fixed input.
    /// </summary>
    public DevelopmentControlReadPayload Project()
    {
        if (!File.Exists(_workbookPath))
        {
            // A workbook that is not there is NOT an empty control. Publishing zeroes would report an
            // authority nobody could reach as an authority that has recorded nothing.
            throw new DevelopmentControlAuthorityUnavailableException(
                $"no DevelopmentControl workbook exists at '{_workbookPath}'. A missing authority is not an "
                + "empty authority: nothing is published, because a valid document reporting zero work items "
                + "would be an answer to a question the authority cannot be asked.");
        }

        var read = WorkbookCompatibilityReader.Read(_workbookPath);
        var gaps = new List<DevelopmentControlReadGap>();

        var workItems = ReadWorkItems(read);
        var changeScopes = ReadChangeScopes(read);
        var changeRequests = ReadChangeRequests(read);
        var dependencies = ReadDependencies(read);
        var lineageEdges = ReadLineageEdges(read, workItems, gaps);

        // `00_Control` binds exactly two logical columns, `ControlItem` and `Value`. SchemaId and
        // SchemaVersion are ROWS in that sheet, not columns — read through the projection the reader
        // already exposes. Measured: reading them as columns returns the empty string, which would
        // publish a control whose schema identity is blank while every other field looks correct.
        var controlItems = WorkbookCompatibilityReader.ControlItems(read);

        var control = new DevelopmentControlReadControl(
            WorkbookCompatibilityReader.AuthorityMarker(read) ?? "UNREADABLE",
            controlItems.TryGetValue("SchemaId", out var schemaId) ? schemaId : string.Empty,
            controlItems.TryGetValue("SchemaVersion", out var schemaVersion) ? schemaVersion : string.Empty,
            read.Sheets.Count(s => s.Presence == SheetPresence.Bound),
            DeliveryAuthorityClass.Authoritative,
            "(the workbook's own recorded state)");

        // A scope that names a work item the control does not hold is a gap, not a silently dropped
        // row: the scope is real, the link is broken, and a consumer that saw only the surviving half
        // would conclude the scope had no item.
        var workItemIds = new HashSet<string>(workItems.Select(w => w.WorkItemId), StringComparer.Ordinal);
        foreach (var scope in changeScopes.Where(s => s.WorkItemId.Length != 0 && !workItemIds.Contains(s.WorkItemId)))
        {
            gaps.Add(new DevelopmentControlReadGap(
                scope.ChangeScopeId,
                "SCOPE_NAMES_UNKNOWN_WORK_ITEM",
                $"Change scope '{scope.ChangeScopeId}' names work item '{scope.WorkItemId}', which the control "
                + "does not hold. The scope is real and the link is broken; the row is published and the "
                + "broken link is reported rather than the scope being dropped."));
        }

        var dependencyIds = new HashSet<string>(workItemIds, StringComparer.Ordinal);
        foreach (var dependency in dependencies)
        {
            foreach (var end in new[] { dependency.FromWorkItemId, dependency.ToWorkItemId })
            {
                if (end.Length != 0 && !dependencyIds.Contains(end))
                {
                    gaps.Add(new DevelopmentControlReadGap(
                        dependency.FromWorkItemId + " -> " + dependency.ToWorkItemId,
                        "DEPENDENCY_NAMES_UNKNOWN_WORK_ITEM",
                        $"A dependency edge names work item '{end}', which the control does not hold."));
                }
            }
        }

        return new DevelopmentControlReadPayload(
            control, workItems, changeScopes, changeRequests, dependencies, lineageEdges, gaps);
    }

    /// <summary>
    /// The semantic digest: SHA-256 over the canonical payload <b>only</b>, so timestamps cannot
    /// corrupt content identity and a metadata refresh is distinguishable from a state change.
    /// </summary>
    public static string SemanticDigest(DevelopmentControlReadPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var json = JsonSerializer.Serialize(payload, CanonicalOptions);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    // ================================================================ readers

    private static List<DevelopmentControlReadWorkItem> ReadWorkItems(WorkbookReadResult read) =>
        Records(read, "WorkGraph").Select(r => new DevelopmentControlReadWorkItem(
            r.Get("WorkId") ?? string.Empty,
            r.Get("Title") ?? string.Empty,
            r.Get("Status") ?? string.Empty,
            r.Get("Owner") ?? string.Empty,
            r.Get("HeadId") ?? string.Empty,
            r.Get("LayerId") ?? string.Empty,
            r.Get("ReadinessState") ?? string.Empty,
            r.Get("AcceptanceCriteria") ?? string.Empty,
            r.Get("EvidenceRequired") ?? string.Empty,
            r.Get("WorkType") ?? string.Empty,
            r.Get("Priority") ?? string.Empty,
            r.Get("ParentWorkId") ?? string.Empty,
            Split(r.Get("DependsOn")),
            DeliveryAuthorityClass.Authoritative))
        .Where(w => w.WorkItemId.Length != 0)
        .OrderBy(w => w.WorkItemId, StringComparer.Ordinal)
        .ToList();

    private static List<DevelopmentControlReadChangeScope> ReadChangeScopes(WorkbookReadResult read) =>
        Records(read, "ChangeScopes").Select(r => new DevelopmentControlReadChangeScope(
            r.Get("ScopeRecordId") ?? string.Empty,
            r.Get("WorkId") ?? string.Empty,
            r.Get("ScopeClass") ?? string.Empty,
            r.Get("Target") ?? string.Empty,
            r.Get("Access") ?? string.Empty,
            r.Get("ReviewState") ?? string.Empty,
            DeliveryAuthorityClass.Authoritative))
        .Where(s => s.ChangeScopeId.Length != 0)
        .OrderBy(s => s.ChangeScopeId, StringComparer.Ordinal)
        .ToList();

    private static List<DevelopmentControlReadChangeRequest> ReadChangeRequests(WorkbookReadResult read) =>
        Records(read, "ChangeRequests").Select(r => new DevelopmentControlReadChangeRequest(
            r.Get("RequestId") ?? string.Empty,
            r.Get("RequestType") ?? string.Empty,
            r.Get("RequestingWorkId") ?? string.Empty,
            r.Get("Destination") ?? string.Empty,
            r.Get("CapabilityRequested") ?? string.Empty,
            r.Get("Status") ?? string.Empty,
            DeliveryAuthorityClass.Authoritative))
        .Where(c => c.RequestId.Length != 0)
        .OrderBy(c => c.RequestId, StringComparer.Ordinal)
        .ToList();

    private static List<DevelopmentControlReadDependency> ReadDependencies(WorkbookReadResult read) =>
        Records(read, "Dependencies").Select(r => new DevelopmentControlReadDependency(
            r.Get("SourceId") ?? string.Empty,
            r.Get("TargetId") ?? string.Empty,
            r.Get("RelationType") ?? string.Empty,
            r.Get("Status") ?? string.Empty,
            DeliveryAuthorityClass.Authoritative))
        .Where(d => d.FromWorkItemId.Length != 0 && d.ToWorkItemId.Length != 0)
        .OrderBy(d => d.FromWorkItemId, StringComparer.Ordinal)
        .ThenBy(d => d.ToWorkItemId, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// The lineage the control actually holds: <c>13_GitLineage</c> rows.
    ///
    /// <para>
    /// Each row is a set of edges, and only the edges whose endpoints the row really carries are
    /// emitted. <b>The row is the authority for the whole chain</b> — WorkItem → ChangeScope →
    /// Repository → CommitSha — which is what makes the <c>CommitSha</c> a value the Delivery
    /// publication also carries, and therefore the one identifier the two authorities can be joined
    /// on. See <c>LINEAGE_JOIN_CONTRACT.md</c>.
    /// </para>
    /// </summary>
    private static List<DevelopmentControlReadLineageEdge> ReadLineageEdges(
        WorkbookReadResult read,
        IReadOnlyList<DevelopmentControlReadWorkItem> workItems,
        List<DevelopmentControlReadGap> gaps)
    {
        var edges = new List<DevelopmentControlReadLineageEdge>();
        var known = new HashSet<string>(workItems.Select(w => w.WorkItemId), StringComparer.Ordinal);

        foreach (var record in Records(read, "GitLineage"))
        {
            var lineageId = record.Get("LineageId") ?? string.Empty;
            var workId = record.Get("WorkId") ?? string.Empty;
            var changeId = record.Get("ChangeId") ?? string.Empty;
            var repositoryId = record.Get("RepositoryId") ?? string.Empty;
            var commitSha = record.Get("CommitSha") ?? string.Empty;
            var branch = record.Get("Branch") ?? string.Empty;

            Add(edges, workId, lineageId, "WORK_ITEM_RECORDED_AS_LINEAGE");
            Add(edges, lineageId, changeId, "LINEAGE_AUTHORISES_CHANGE");
            Add(edges, changeId, repositoryId, "CHANGE_TOUCHES_REPOSITORY");
            Add(edges, repositoryId, commitSha, "REPOSITORY_AT_COMMIT");
            Add(edges, commitSha, branch, "COMMIT_ON_BRANCH");

            // A lineage row naming a work item the control does not hold is a gap, not a dropped row.
            if (workId.Length != 0 && !known.Contains(workId))
            {
                gaps.Add(new DevelopmentControlReadGap(
                    lineageId,
                    "LINEAGE_NAMES_UNKNOWN_WORK_ITEM",
                    $"Lineage record '{lineageId}' names work item '{workId}', which the control does not hold."));
            }

            // A row with no CommitSha cannot be joined to Delivery at all, and saying so is the point:
            // the alternative is a consumer inventing the commit from the branch or the timestamp.
            if (commitSha.Length == 0)
            {
                gaps.Add(new DevelopmentControlReadGap(
                    lineageId,
                    "LINEAGE_HAS_NO_SOURCE_COMMIT",
                    $"Lineage record '{lineageId}' carries no CommitSha, so it cannot be joined to a Delivery "
                    + "release. The join is the ONLY cross-authority link, and it is absent here rather than "
                    + "approximated."));
            }
        }

        return edges
            .OrderBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ThenBy(e => e.Kind, StringComparer.Ordinal)
            .ToList();
    }

    private static void Add(List<DevelopmentControlReadLineageEdge> edges, string from, string to, string kind)
    {
        // Both endpoints must be present. An edge with an empty endpoint is not an edge — it is the
        // absence of one, and emitting it would put a blank node in a graph and read as a real link.
        if (from.Length == 0 || to.Length == 0)
        {
            return;
        }

        edges.Add(new DevelopmentControlReadLineageEdge(from, to, kind, DeliveryAuthorityClass.Authoritative));
    }

    // ================================================================ helpers

    private static IEnumerable<WorkbookRecord> Records(WorkbookReadResult read, string logicalSheet) =>
        read.Sheet(logicalSheet)?.Records ?? [];


    /// <summary>Splits a multi-value cell. Commas and semicolons, trimmed, blanks dropped.</summary>
    private static List<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Distinct(StringComparer.Ordinal)
                   .OrderBy(v => v, StringComparer.Ordinal)
                   .ToList();
}

/// <summary>
/// The authority cannot be asked. <b>Distinct from an authority that answered "none".</b>
/// </summary>
public sealed class DevelopmentControlAuthorityUnavailableException : Exception
{
    public DevelopmentControlAuthorityUnavailableException(string message) : base(message) { }
}
