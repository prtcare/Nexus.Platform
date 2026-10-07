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

        // W10.5B. A workbook the reader could not RECOGNISE is not a control with nothing in it.
        //
        // WHY THIS BELONGS HERE AND NOT ONLY IN THE READER. The reader already refuses to call an
        // unrecognised workbook Supported, and it names the reason — but `Project()` still built a
        // payload from whatever sheets happened to resolve, and the publisher published it. The
        // result was a document that looked entirely valid and reported a control assembled from a
        // read the reader had explicitly failed. This is the same failure the reader's own comment
        // calls out one layer down ("recognised, read the wrong thing, returned nothing, called it
        // success"), one layer up and one step worse: not recognised, and published anyway.
        //
        // NARROW ON PURPOSE. Only UnsupportedSchema and Corrupt refuse. EmptyValid is a recognised,
        // well-formed workbook that governs nothing — a legitimate thing to publish — and
        // PartiallySupported carries named, reported gaps that the payload already describes.
        // Refusing those would replace an honest document with no document, which is the trade this
        // milestone exists to avoid making.
        if (read.Result is ReaderResult.UnsupportedSchema or ReaderResult.Corrupt)
        {
            throw new DevelopmentControlAuthorityUnavailableException(
                $"the workbook at '{_workbookPath}' was not recognised by the canonical reader "
                + $"(result={read.Result}). Publishing a read model from a read that failed would produce a "
                + "document that is structurally valid and describes a control nobody could identify. "
                + string.Join(" ", read.Diagnostics));
        }

        var gaps = new List<DevelopmentControlReadGap>();

        var workItems = ReadWorkItems(read);
        var changeScopes = ReadChangeScopes(read);
        var changeRequests = ReadChangeRequests(read);
        var dependencies = ReadDependencies(read);
        var lineageEdges = ReadLineageEdges(read, workItems, gaps);
        var governance = ReadGovernanceRegistry(read, gaps);

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

        // W10.5B. Emitted unconditionally, and it is the one gap here that is a fact about the
        // Component rather than about this workbook: `Nexus.Governance` computes a verdict and
        // returns it, and nothing persists it. There is no file I/O anywhere in `src/03-Governance`.
        //
        // WHY UNCONDITIONAL IS THE HONEST CHOICE. It is tempting to emit this only when the registry
        // is populated, on the reasoning that an empty registry has no history to be missing. That
        // reasoning is wrong in the direction that matters: the absence is not a property of these
        // rows, it is a property of the estate. A consumer asking "what did governance decide about
        // this gate?" gets the same answer — nothing is recorded — whether the registry holds 91
        // gates or none, and suppressing the statement for the empty case would let a view render
        // "no decisions" for a source that has no decision history at all.
        gaps.Add(new DevelopmentControlReadGap(
            DevelopmentControlReadGapKinds.GovernanceRegistrySubject,
            DevelopmentControlReadGapKinds.GovernanceEvaluationHistorySourceGap,
            "Governance evaluates a request and returns a verdict; nothing durably records it, so no "
            + "historical governance decision exists to publish. This is an ABSENCE, not a zero: the "
            + "registry below records WHICH GATES EXIST and their planning status, and it does not "
            + "record any approval, refusal or deferral. A runtime evaluation result must not be "
            + "presented as historical governance authority."));

        // W10.5B TASK 10. Measured from the records rather than asserted, and emitted only when the
        // registry was actually read: a workbook whose governance sheet is missing has no rows whose
        // identifiers could be inspected, and reporting "no shared identifier exists" for a surface
        // that could not be read would be a measurement nobody took.
        if (governance.State == DevelopmentControlReadGovernanceRegistry.Available
            && governance.Gates.Count > 0
            && !governance.Gates.Any(HasCrossSourceIdentifier))
        {
            gaps.Add(new DevelopmentControlReadGap(
                DevelopmentControlReadGapKinds.GovernanceRegistrySubject,
                DevelopmentControlReadGapKinds.GovernanceCrossSourceJoinGap,
                $"None of the {governance.Gates.Count} registry record(s) carries an identifier any other "
                + "contract also publishes: GateId, ChangeId (envelope) and SupersedesVersion are blank on "
                + "every record. Governance therefore cannot be related to a work item, change scope, "
                + "release or deployment. BlocksScope and Notes are free text and are NOT keys; nor are "
                + "name, timestamp, row position or any fuzzy match, and none is substituted here."));
        }

        return new DevelopmentControlReadPayload(
            control, workItems, changeScopes, changeRequests, dependencies, lineageEdges, governance, gaps);
    }

    /// <summary>
    /// W10.5B TASK 10's measurement, in one place. <b>The three fields that could carry a shared
    /// identifier, and only those three.</b>
    ///
    /// <para>
    /// Deliberately not extended to <c>Name</c>, <c>Notes</c>, <c>BlocksScope</c>,
    /// <c>SourceRecordId</c> or <c>EffectiveFrom</c>. Each of those has a plausible-looking
    /// relationship to something else in the estate and none of them is one: <c>Name</c> is not
    /// unique (six names recur), <c>BlocksScope</c> is free text such as <c>M-15..M-19</c> whose
    /// milestone ranges would have to be parsed out of prose, and <c>SourceRecordId</c> identifies a
    /// row in a LEGACY workbook's own sheet rather than anything this contract publishes. Treating
    /// any of them as a key is the forbidden inference, so the measurement is written narrowly and a
    /// future reader can see exactly what was and was not counted.
    /// </para>
    /// </summary>
    private static bool HasCrossSourceIdentifier(DevelopmentControlReadGovernanceGate gate) =>
        gate.GateId.Length != 0
        || gate.EnvelopeChangeId.Length != 0
        || gate.SupersedesVersion.Length != 0;

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

    /// <summary>
    /// W10.5B — reads <c>18_Governance</c> through the binding this milestone added, and carries the
    /// registry's own availability beside its rows.
    ///
    /// <para>
    /// <b>A missing sheet is not an empty registry.</b> The binding DECLARES <c>18_Governance</c>, so
    /// a V3 workbook without it produces <see cref="SheetPresence.DeclaredButMissing"/> — a named,
    /// reported outcome — and this method turns that into <c>UNAVAILABLE</c> with the reason attached.
    /// The alternative, returning an empty list, would publish a valid document asserting that the
    /// control governs nothing, which is an answer to a question the authority was never asked.
    /// </para>
    ///
    /// <para>
    /// <b>A blank or duplicated <c>GovernanceId</c> refuses the publication.</b> This is the one
    /// refusal here that is not about reachability. <c>GovernanceId</c> is the registry's declared
    /// immutable identity and the collection is keyed by it; a row without one cannot be addressed,
    /// and two rows sharing one make every "this gate" question ambiguous. Both are refused rather
    /// than repaired, dropped or de-duplicated, because each repair would produce a document whose
    /// gate count is confidently wrong — the failure this whole milestone exists to make impossible.
    /// The refusal is loud and names the offending row, so the malformed record is findable.
    /// </para>
    /// </summary>
    private static DevelopmentControlReadGovernanceRegistry ReadGovernanceRegistry(
        WorkbookReadResult read, List<DevelopmentControlReadGap> gaps)
    {
        var sheet = read.Sheet("GovernanceGates");

        if (sheet is null || sheet.Presence != SheetPresence.Bound)
        {
            var why = sheet is null
                ? "no logical sheet named 'GovernanceGates' resolves on this form"
                : $"the sheet is {sheet.Presence}";

            return new DevelopmentControlReadGovernanceRegistry(
                DevelopmentControlReadGovernanceRegistry.Unavailable,
                $"The Governance Gate Registry could not be read: {why}"
                + (sheet?.PhysicalName is null ? "" : $" (physical sheet '{sheet.PhysicalName}')")
                + ". This is an ABSENT SOURCE, not a registry holding no gates, and the empty list below "
                + "asserts nothing. The two must not be rendered the same way.",
                []);
        }

        var gates = new List<DevelopmentControlReadGovernanceGate>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var record in sheet.Records)
        {
            var governanceId = (record.Get("GovernanceId") ?? string.Empty).Trim();

            if (governanceId.Length == 0)
            {
                throw new DevelopmentControlGovernanceRegistryInvalidException(
                    $"The Governance Gate Registry carries a record at row {record.Row} of '{sheet.PhysicalName}' "
                    + "with no GovernanceId. That column is the registry's immutable identity and the collection "
                    + "is keyed by it, so a record without one cannot be addressed. It is NOT skipped: dropping it "
                    + "would publish a gate count that is confidently wrong, and the count is what a consumer would "
                    + "act on. The record must be corrected in the authority.");
            }

            if (seen.TryGetValue(governanceId, out var firstRow))
            {
                throw new DevelopmentControlGovernanceRegistryInvalidException(
                    $"The Governance Gate Registry declares GovernanceId '{governanceId}' at rows {firstRow} and "
                    + $"{record.Row} of '{sheet.PhysicalName}'. A duplicated identity makes every question about "
                    + "'this gate' ambiguous, so neither record is published and neither is preferred. The "
                    + "duplicate must be resolved in the authority.");
            }

            seen[governanceId] = record.Row;

            gates.Add(new DevelopmentControlReadGovernanceGate(
                governanceId,
                record.Get("GateId") ?? string.Empty,
                record.Get("Name") ?? string.Empty,
                record.Get("AuthorityProfile") ?? string.Empty,
                record.Get("RequiredEvidence") ?? string.Empty,
                // RAW. Published exactly as the authority spells it — see the record's own doc for why
                // normalising the fourteen measured planning states would be a false claim.
                record.Get("RegistryStatus") ?? string.Empty,
                record.Get("BlocksScope") ?? string.Empty,
                record.Get("Notes") ?? string.Empty,
                record.Get("IsCurrent") ?? string.Empty,
                record.Get("EffectiveFrom") ?? string.Empty,
                record.Get("EnvelopeChangeId") ?? string.Empty,
                record.Get("SupersedesVersion") ?? string.Empty,
                new DevelopmentControlReadGovernanceSource(
                    record.Get("SourceForm") ?? string.Empty,
                    record.Get("SourceWorkbook") ?? string.Empty,
                    record.Get("SourceWorkbookHash") ?? string.Empty,
                    record.Get("SourceSheet") ?? string.Empty,
                    record.Get("SourceRecordId") ?? string.Empty,
                    record.Get("SourceRevision") ?? string.Empty,
                    record.Get("SourceArchitectureVersion") ?? string.Empty,
                    record.Get("MigrationTimestamp") ?? string.Empty,
                    record.Get("MigrationTransformation") ?? string.Empty),
                DeliveryAuthorityClass.Authoritative));
        }

        return new DevelopmentControlReadGovernanceRegistry(
            DevelopmentControlReadGovernanceRegistry.Available,
            $"read from '{sheet.PhysicalName}', header row {sheet.HeaderRow}, "
            + $"{gates.Count} registry record(s). The sheet is bound READ-ONLY and is not an append target.",
            gates.OrderBy(g => g.GovernanceId, StringComparer.Ordinal).ToList());
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

/// <summary>
/// W10.5B. The authority was reached and read, and the Governance Gate Registry it returned cannot
/// be published. <b>Kept distinct from <see cref="DevelopmentControlAuthorityUnavailableException"/>
/// on purpose:</b> "I could not reach the authority" and "the authority answered and its answer is
/// unusable" are different facts with different remedies, and collapsing them would send whoever
/// reads the failure to the wrong place. This one is always a defect in the registry itself — a
/// missing or duplicated immutable identity — and the message names the row.
/// </summary>
public sealed class DevelopmentControlGovernanceRegistryInvalidException : Exception
{
    public DevelopmentControlGovernanceRegistryInvalidException(string message) : base(message) { }
}
