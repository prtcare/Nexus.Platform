using System.Text;
using Nexus.ProductCore.Contracts.DevelopmentControl;

namespace Nexus.ProductCore.Core.DevelopmentControl;

/// <summary>
/// W8D-R5 TASK 8. Renders a deterministic, ordered vector of how this component INTERPRETS a
/// workbook, so two hosts reading the same workbook can be compared by diffing two files.
///
/// <para><b>What this proves and what it does not.</b> A byte-identical diff proves that both
/// hosts, running as separate processes from separate builds, resolved every scope, acceptance,
/// change-request and dependency value the same way. It does not prove either host's build is
/// current — that is a separate check, because a stale build on one side would make this diff fail
/// and be misread as an interpretation divergence. Callers must establish build identity first;
/// the Developer-side cross-host test does.</para>
///
/// <para><b>Why the renderer lives here rather than in each host.</b> If each host carried its own
/// formatter, the diff would compare two formatters as well as two interpretations, and a
/// formatting drift would be indistinguishable from a semantic one — the estate has already paid
/// for that class of confusion twice (the apphost-for-assembly harness path, and the stale-harness
/// comparison). One renderer eliminates the variable instead of documenting it. What remains
/// independent is genuinely independent: the process, the build, and the loading of the assembly.
/// The Developer-side suite additionally asserts the loaded assembly is the shared one, so this
/// cannot degenerate into "the same helper agreed with itself".</para>
///
/// <para><b>No path, hash, timestamp or host identity appears in the output.</b> Those differ
/// between two hosts by construction — each writes to its own disposable copy — and including them
/// would make every diff differ for reasons that have nothing to do with interpretation.</para>
/// </summary>
public static class DevelopmentControlInterpretationVectors
{
    /// <summary>Format version. Bump when the row shape changes, so a diff between two hosts
    /// running different component versions fails on the version line rather than on a row that
    /// merely moved.</summary>
    public const string FormatVersion = "W8DR5-INTERPRETATION-VECTORS v1";

    /// <summary>
    /// The fixed collision probe. Declared here, in one place, so both hosts evaluate the SAME
    /// input by construction rather than each inventing a comparable-looking set.
    ///
    /// <para>This is a probe, not model data: it does not come from any workbook. It exists because
    /// the collision policy is the surface that answers "would this scope change collide", and that
    /// answer has to be shown to be identical on both hosts. The inputs are chosen to exercise each
    /// verdict the policy can return, including the fail-closed one.</para>
    /// </summary>
    private static readonly ChangeScopeDeclaration[] CollisionProbe =
    [
        new("probe-lane-a", "probe-change-a",
        [
            new ChangeScopeItem(ChangeScopeItemKind.ExactFile, ChangeScopeAccessMode.Write, "src/shared/file.cs"),
            new ChangeScopeItem(ChangeScopeItemKind.Glob, ChangeScopeAccessMode.Write, "src/only-a/**"),
            new ChangeScopeItem(ChangeScopeItemKind.DirectorySubtree, ChangeScopeAccessMode.Read, "src/read-only/"),
        ]),
        new("probe-lane-b", "probe-change-b",
        [
            new ChangeScopeItem(ChangeScopeItemKind.ExactFile, ChangeScopeAccessMode.Write, "src/shared/file.cs"),
            new ChangeScopeItem(ChangeScopeItemKind.Glob, ChangeScopeAccessMode.Write, "src/only-b/**"),
            new ChangeScopeItem(ChangeScopeItemKind.DirectorySubtree, ChangeScopeAccessMode.Read, "src/read-only/"),
        ]),
    ];

    /// <summary>Renders the vector. Line endings are <c>\n</c> on every platform and the caller
    /// should write the result as UTF-8 without a BOM, so the two files differ only if the
    /// interpretations do.</summary>
    public static string Dump(DevelopmentControlReadResult read)
    {
        var sb = new StringBuilder();
        var lookup = new DevelopmentControlLookup(read);

        sb.Append(FormatVersion).Append('\n');
        sb.Append("FORM=").Append(read.Form).Append('\n');
        sb.Append("AUTHORITY=").Append(read.Authority).Append('\n');
        sb.Append("SCHEMA=").Append(read.SchemaId).Append('|').Append(read.SchemaVersion).Append('\n');

        // ---- coverage, in binding order so the row sequence is a property of the map.
        foreach (var sheet in read.Sheets)
        {
            var c = read.Coverage(sheet.LogicalName);
            sb.Append("COVERAGE|").Append(sheet.LogicalName)
              .Append("|readable=").Append(Flag(c?.Readable))
              .Append("|fullyProjected=").Append(Flag(c?.FullyProjected))
              .Append("|carriesData=").Append(Flag(c?.CarriesData))
              .Append("|unprojected=").Append(c?.UnprojectedColumns.Count ?? -1)
              .Append("|records=").Append(c?.RecordCount ?? -1)
              .Append('\n');
        }

        sb.Append("AVAIL")
          .Append("|lineage=").Append(Flag(!lookup.LineageUnavailable))
          .Append("|scopes=").Append(Flag(!lookup.ChangeScopesUnavailable))
          .Append("|requests=").Append(Flag(!lookup.ChangeRequestsUnavailable))
          .Append("|acceptance=").Append(Flag(!lookup.AcceptanceUnavailable))
          .Append('\n');

        // ---- dependency edges, sorted so enumeration order cannot differ between hosts.
        var edges = read.Records
            .Where(r => r.LogicalSheet == "Dependencies")
            .Select(r => new DevelopmentControlDependency(
                r.Get("SourceId") ?? "", r.Get("TargetId") ?? "",
                r.Get("RelationType") ?? "", r.Get("Status") ?? ""))
            .OrderBy(e => e.FromWorkItemId, StringComparer.Ordinal)
            .ThenBy(e => e.ToWorkItemId, StringComparer.Ordinal)
            .ToArray();

        foreach (var e in edges)
            sb.Append("DEP|").Append(e.FromWorkItemId).Append('|').Append(e.ToWorkItemId)
              .Append('|').Append(e.Kind).Append('|').Append(e.Status).Append('\n');

        // ---- ChangeScope rows, sorted by key. The parsed enums are rendered as the member name or
        // `-` when the vocabulary did not bridge - never as a defaulted member.
        foreach (var s in AllScopes(read, lookup))
            sb.Append("SCOPE|").Append(s.ScopeRecordId).Append('|').Append(s.WorkItemId)
              .Append('|').Append(s.ScopeClass)
              .Append('|').Append(s.ItemTypeText)
              .Append('|').Append(s.ItemType?.ToString() ?? "-")
              .Append('|').Append(s.AccessText)
              .Append('|').Append(s.Access?.ToString() ?? "-")
              .Append('|').Append(s.ReviewState)
              .Append('|').Append(Tri(s.IsCurrent))
              .Append("|wellFormed=").Append(Flag(s.WellFormed))
              .Append('\n');

        // ---- acceptance state for every work item the V3 sheet carries.
        foreach (var id in WorkItemIds(read))
            sb.Append("ACC|").Append(id)
              .Append("|criteriaDeclared=").Append(Flag(lookup.AcceptanceOf(id)?.CriteriaDeclared))
              .Append("|evidenceNamed=").Append(Flag(lookup.AcceptanceOf(id)?.EvidenceNamed))
              .Append("|readiness=").Append(lookup.AcceptanceOf(id)?.ReadinessState ?? "")
              .Append('\n');

        // ---- Core Change Requests, sorted by key.
        var requests = read.Records
            .Where(r => r.LogicalSheet == "ChangeRequests")
            .Select(r => new DevelopmentControlChangeRequest(
                r.Get("RequestId") ?? "", r.Get("RequestType") ?? "", r.Get("Destination") ?? "",
                r.Get("RequestingWorkId") ?? "", r.Get("RequestingHead") ?? "",
                r.Get("CapabilityRequested") ?? "", r.Get("Purpose") ?? "", r.Get("ContextRefs") ?? "",
                r.Get("DataClassification") ?? "", r.Get("ExecutionPolicy") ?? "",
                r.Get("ToolPermissionProfile") ?? "", r.Get("Status") ?? "",
                r.Get("PlatformChangeRequestId") ?? "", r.Get("HandbackWorkId") ?? "",
                r.Get("LegacyStatusText") ?? "", r.Get("IsCurrent") ?? "", r.Get("Notes") ?? ""))
            .OrderBy(r => r.RequestId, StringComparer.Ordinal)
            .ToArray();

        foreach (var r in requests)
            sb.Append("CREQ|").Append(r.RequestId).Append('|').Append(r.RequestType)
              .Append('|').Append(r.Destination).Append('|').Append(r.RequestingWorkId)
              .Append('|').Append(r.Status).Append('|').Append(r.LegacyStatusText)
              .Append('|').Append(Tri(r.IsCurrent))
              .Append('\n');

        // ---- Git lineage, sorted by key. Empty on the V3 candidate, and rendered as a count so
        // "the surface is empty" is visible in the diff rather than looking like an omission.
        var lineage = read.Records
            .Where(r => r.LogicalSheet == "GitLineage")
            .Select(r => r.Get("LineageId") ?? "")
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        sb.Append("LINEAGE-COUNT=").Append(lineage.Length).Append('\n');

        // ---- the collision policy, on the fixed probe.
        var policy = new DevelopmentControlChangeScopePolicy();
        var result = policy.Evaluate(CollisionProbe);
        sb.Append("COLLIDE-COMPATIBLE=").Append(Flag(result.Compatible)).Append('\n');
        foreach (var c in result.Conflicts
                     .OrderBy(c => c.LeftLane, StringComparer.Ordinal)
                     .ThenBy(c => c.RightLane, StringComparer.Ordinal)
                     .ThenBy(c => c.Resource, StringComparer.Ordinal))
        {
            sb.Append("COLLIDE|").Append(c.LeftLane).Append('|').Append(c.RightLane)
              .Append('|').Append(c.Resource).Append('|').Append(c.Verdict).Append('\n');
        }

        return sb.ToString();
    }

    private static IEnumerable<DevelopmentControlScopeRow> AllScopes(
        DevelopmentControlReadResult read, DevelopmentControlLookup lookup) =>
        read.Sheet("ChangeScopes") is not { Present: true }
            ? []
            : WorkItemIds(read, "ChangeScopes")
                .SelectMany(id => lookup.ScopesOf(id))
                .OrderBy(s => s.ScopeRecordId, StringComparer.Ordinal);

    private static IEnumerable<string> WorkItemIds(
        DevelopmentControlReadResult read, string logicalSheet = "WorkGraph") =>
        (read.Sheet(logicalSheet)?.Records ?? [])
            .Select(r => r.Get("WorkId"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal);

    private static string Flag(bool? value) => value switch
    {
        true => "1",
        false => "0",
        null => "-",
    };

    private static string Tri(bool? value) => Flag(value);
}
