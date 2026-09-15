// ReadinessGate.cs — W1-06 PARALLEL-SAFETY ENFORCEMENT + W1-07 BLANK DEPENDENCY SAFETY.
//
// TWO SEPARATE FALSE-ASSURANCE DEFECTS, ONE GATE
// ----------------------------------------------
// W1-06: `ParallelSafe` is a first-class field on Node (Node.cs:22) and a real workbook
// column (ExcelWorkbookColumnMap.cs:38), and it has no influence on execution. It is a
// DISPLAY VALUE. A planner marks a row parallel-safe and the row becomes startable, because
// nothing consumes the flag when deciding whether work may begin. A field that can be read
// but not acted on is worse than no field: it is a field somebody will rely on.
//
// W1-07: a blank `DependsOn` is currently indistinguishable from "no dependencies". The
// workbook cannot tell "I checked, there are none" from "nobody has filled this in yet", and
// the dependency engine reads both as the second. V3_M1D_DEPENDENCY_POLICY.md freezes the
// rule: BLANK MEANS UNKNOWN, and UNKNOWN IS NOT DEPENDENCY-FREE.
//
// The fix for both is the same shape: execution permission becomes a CALCULATED value with
// typed blockers, and every display field is demoted to a hint that can only ever make the
// answer more conservative — never less.
namespace Nexus.DevelopmentControl.Safety;

/// <summary>The three-state dependency answer. There is deliberately no two-state version:
/// collapsing Unknown into false is precisely the W1-07 defect.</summary>
public enum DependencyState
{
    Unknown = 0,
    DependencyFree = 1,
    Satisfied = 2,
    Unsatisfied = 3,
}

public enum ReadinessBlocker
{
    DependencyUnknown = 0,
    DependencyUnsatisfied = 1,
    DependencyCycle = 2,
    GovernancePrerequisiteMissing = 3,
    ConflictingReservation = 4,
    ScopeInvalid = 5,
    AuthorityProfileDisallows = 6,
    BaselineNotCaptured = 7,
    BaselineStale = 8,
    WorkItemNotInStartableStatus = 9,
    ParallelSafeClaimedWithoutComputation = 10,
}

public sealed record ReadinessBlockerDetail(ReadinessBlocker Blocker, string Detail);

public sealed record ReadinessDecision(
    bool Ready,
    string Reason,
    IReadOnlyList<ReadinessBlockerDetail> Blockers,
    IReadOnlyList<string> Evidence)
{
    /// <summary>Present so a caller cannot treat a non-empty blocker list as advisory.</summary>
    public bool HasBlockers => Blockers.Count > 0;
}

/// <summary>
/// A dependency declaration as READ FROM A SOURCE. <see cref="WasBlank"/> is retained because
/// the difference between "blank" and "explicitly none" is the whole of W1-07 — and because
/// legacy rows must not be silently rewritten in W1; they must be surfaced as UNKNOWN.
/// </summary>
public sealed record DependencyDeclaration(string? Raw)
{
    /// <summary>Sentinel spellings that mean "explicitly none". Deliberately a closed,
    /// visible set: widening it is a governance decision, not a parsing convenience.</summary>
    private static readonly string[] ExplicitNone = { "none", "-", "n/a", "[]", "(none)", "no dependencies" };

    public bool WasBlank => string.IsNullOrWhiteSpace(Raw);

    /// <summary>
    /// True when a declaration token EXPLICITLY says "no dependencies". A sentinel is not an
    /// endpoint, so graph analysis must not treat it as one: without this, a node that declared
    /// `NONE` would be reported as depending on an id that resolves to nothing, and the explicitly
    /// dependency-free state W1-07 requires could never be expressed in a dependency graph.
    /// </summary>
    public static bool IsExplicitNone(string? raw) =>
        raw is not null && ExplicitNone.Contains(raw.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The W1-07 rule, in one expression. A blank declaration is UNKNOWN. It becomes
    /// DependencyFree only when the source EXPLICITLY said so.
    /// </summary>
    public DependencyState Classify(IReadOnlyCollection<string>? unresolvedIds, IReadOnlyCollection<string>? completedIds)
    {
        if (WasBlank) return DependencyState.Unknown;

        var trimmed = Raw!.Trim();
        if (IsExplicitNone(trimmed))
            return DependencyState.DependencyFree;

        var ids = trimmed
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (ids.Length == 0) return DependencyState.Unknown;

        var unresolved = unresolvedIds ?? Array.Empty<string>();
        var completed = completedIds ?? Array.Empty<string>();

        // A dependency the ledger cannot resolve at all is UNKNOWN, not satisfied. This is the
        // same rule as a blank one, applied one level down: an id nothing recognises is not
        // evidence of completion.
        if (ids.Any(id => !unresolved.Contains(id, StringComparer.OrdinalIgnoreCase)
                       && !completed.Contains(id, StringComparer.OrdinalIgnoreCase)))
            return DependencyState.Unknown;

        return ids.Any(id => unresolved.Contains(id, StringComparer.OrdinalIgnoreCase))
            ? DependencyState.Unsatisfied
            : DependencyState.Satisfied;
    }
}

/// <summary>
/// The planning hint. Renamed from `ParallelSafe` on purpose: a boolean named
/// `ParallelSafe` invites a caller to treat it as an answer, and this type exists to make
/// clear that it is an input that can only ever be corroborated.
/// </summary>
public sealed record ParallelLaneHint(
    bool DeclaredParallelSafe,
    string? Lane,
    string? DeclaredBy,
    DateTimeOffset? DeclaredAtUtc)
{
    public static readonly ParallelLaneHint None = new(false, null, null, null);
}

public sealed record ReadinessRequest
{
    public required string WorkId { get; init; }
    public required string Status { get; init; }
    public DependencyDeclaration? Dependencies { get; init; }
    public IReadOnlyCollection<string>? UnresolvedDependencyIds { get; init; }
    public IReadOnlyCollection<string>? CompletedDependencyIds { get; init; }
    public IReadOnlyList<string>? DependencyCycle { get; init; }
    public bool GovernancePrerequisitesSatisfied { get; init; }
    public bool? GovernancePrerequisitesKnown { get; init; }
    public IReadOnlyList<string>? ConflictingReservationIds { get; init; }
    public bool ScopeValid { get; init; }
    public bool? ScopeValidKnown { get; init; }
    public string? AuthorityProfile { get; init; }
    public bool? AuthorityProfilePermitsTarget { get; init; }
    public RepositoryBaseline? Baseline { get; init; }
    public BaselineVerdict? BaselineVerdict { get; init; }
    public ParallelLaneHint? LaneHint { get; init; }
    /// <summary>The calculated collision result. When absent, the gate CANNOT conclude
    /// "no conflict" — it can only conclude that it does not know.</summary>
    public CollisionResult? Collision { get; init; }

    public static readonly string[] StartableStatuses = { "ready", "planned" };
}

public static class ReadinessGate
{
    /// <summary>
    /// The single execution gate. Nothing may start work without a <see cref="ReadinessDecision"/>
    /// whose <see cref="ReadinessDecision.Ready"/> is true, and <c>Ready</c> is computed from
    /// the inputs below — never from a stored flag.
    /// </summary>
    public static ReadinessDecision Evaluate(ReadinessRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);

        var blockers = new List<ReadinessBlockerDetail>();
        var evidence = new List<string> { $"workId={req.WorkId}", $"status={req.Status}" };

        // ---- 1. status
        if (!ReadinessRequest.StartableStatuses.Contains(req.Status?.Trim().ToLowerInvariant() ?? "", StringComparer.Ordinal))
            blockers.Add(new(ReadinessBlocker.WorkItemNotInStartableStatus,
                $"Status '{req.Status}' is not one of Ready/Planned."));

        // ---- 2. dependencies (W1-07)
        var decl = req.Dependencies ?? new DependencyDeclaration(null);
        var depState = decl.Classify(req.UnresolvedDependencyIds, req.CompletedDependencyIds);
        evidence.Add($"dependencyRaw={(decl.WasBlank ? "(blank)" : decl.Raw)} state={depState}");

        switch (depState)
        {
            case DependencyState.Unknown:
                blockers.Add(new(ReadinessBlocker.DependencyUnknown,
                    decl.WasBlank
                        ? "DependsOn is blank. BLANK MEANS UNKNOWN, not dependency-free — the source did not say there are none, it said nothing. " +
                          "This work item must not become READY until a human or the dependency engine records an explicit dependency-free state."
                        : $"The dependency declaration '{decl.Raw}' names at least one id that resolves to neither a completed nor an open work item."));
                break;

            case DependencyState.Unsatisfied:
                blockers.Add(new(ReadinessBlocker.DependencyUnsatisfied,
                    $"Unsatisfied dependencies: {string.Join(", ", req.UnresolvedDependencyIds ?? Array.Empty<string>())}."));
                break;

            case DependencyState.DependencyFree:
                evidence.Add("dependency declared explicitly none");
                break;
        }

        // ---- 3. cycles
        if (req.DependencyCycle is { Count: > 0 })
            blockers.Add(new(ReadinessBlocker.DependencyCycle,
                $"Dependency cycle: {string.Join(" -> ", req.DependencyCycle)}."));

        // ---- 4. governance prerequisites. A MISSING answer is not a satisfied one.
        if (req.GovernancePrerequisitesKnown != true)
            blockers.Add(new(ReadinessBlocker.GovernancePrerequisiteMissing,
                "Governance prerequisite state was not evaluated, so it cannot be treated as satisfied."));
        else if (!req.GovernancePrerequisitesSatisfied)
            blockers.Add(new(ReadinessBlocker.GovernancePrerequisiteMissing,
                "Governance prerequisites are not satisfied."));

        // ---- 5. scope validity
        if (req.ScopeValidKnown != true)
            blockers.Add(new(ReadinessBlocker.ScopeInvalid,
                "The requested scope was not validated."));
        else if (!req.ScopeValid)
            blockers.Add(new(ReadinessBlocker.ScopeInvalid, "The requested scope is not valid."));

        // ---- 6. authority profile
        if (req.AuthorityProfilePermitsTarget != true)
            blockers.Add(new(ReadinessBlocker.AuthorityProfileDisallows,
                req.AuthorityProfilePermitsTarget is null
                    ? "No authority profile was evaluated for the target."
                    : $"Authority profile '{req.AuthorityProfile}' does not permit the target."));

        // ---- 7. baseline captured (W1-04)
        if (req.Baseline is null || string.IsNullOrWhiteSpace(req.Baseline.BaseSHA))
            blockers.Add(new(ReadinessBlocker.BaselineNotCaptured,
                "No BaseSHA was captured for this work item's repository."));
        else if (req.BaselineVerdict is null)
            blockers.Add(new(ReadinessBlocker.BaselineStale,
                "A baseline exists but was not evaluated against the current worktree, so drift cannot be ruled out."));
        else if (!req.BaselineVerdict.RunMayProceed)
            blockers.Add(new(ReadinessBlocker.BaselineStale,
                $"Baseline verdict is {req.BaselineVerdict.State}: {req.BaselineVerdict.Reason}"));

        // ---- 8. collisions. Absent computation is not absence of conflict.
        if (req.Collision is null)
            blockers.Add(new(ReadinessBlocker.ConflictingReservation,
                "Collision against active reservations was not computed."));
        else if (req.Collision.Verdict == CollisionVerdict.Collision)
            blockers.Add(new(ReadinessBlocker.ConflictingReservation, req.Collision.Reason));
        else if (req.Collision.Verdict == CollisionVerdict.Undetermined)
            blockers.Add(new(ReadinessBlocker.ConflictingReservation,
                "Collision could not be determined: " + req.Collision.Reason));

        if (req.ConflictingReservationIds is { Count: > 0 })
            blockers.Add(new(ReadinessBlocker.ConflictingReservation,
                $"Active reservations conflict: {string.Join(", ", req.ConflictingReservationIds)}."));

        // ---- 9. the lane hint. THE POINT OF THE WHOLE TYPE.
        // A lane hint is checked LAST and can only ever ADD a blocker. There is no branch in
        // this method in which DeclaredParallelSafe == true makes Ready true. It cannot
        // outvote a dependency, a conflict or a missing baseline, and a work item that is
        // marked parallel-safe while its dependencies are UNKNOWN is exactly the case that
        // must be blocked — so the hint is recorded as evidence of the contradiction.
        var hint = req.LaneHint ?? ParallelLaneHint.None;
        evidence.Add($"laneHint parallelSafe={hint.DeclaredParallelSafe} lane={hint.Lane ?? "(none)"}");
        if (hint.DeclaredParallelSafe && blockers.Count > 0)
            blockers.Add(new(ReadinessBlocker.ParallelSafeClaimedWithoutComputation,
                "The planning sheet marks this work item ParallelSafe, but calculated readiness found blockers. " +
                "ParallelSafe is a planning hint; it is never evidence that the calculated conditions hold, and it cannot clear a blocker."));

        var ready = blockers.Count == 0;
        var reason = ready
            ? "All gating conditions were computed and satisfied."
            : $"{blockers.Count} blocking condition(s): {string.Join("; ", blockers.Select(b => b.Blocker))}";

        return new ReadinessDecision(ready, reason, blockers, evidence);
    }

    /// <summary>
    /// Detects the dependency graph's cycles, and — just as importantly — reports the ids that
    /// resolve to nothing. An unresolvable endpoint is the graph-level form of a blank
    /// dependency: it looks like an edge and carries no meaning.
    /// </summary>
    public static (IReadOnlyList<string> Cycle, IReadOnlyList<string> UnresolvedEndpoints) AnalyseGraph(
        IReadOnlyDictionary<string, IReadOnlyList<string>> edges)
    {
        var unresolved = new List<string>();
        foreach (var (_, targets) in edges)
            foreach (var t in targets)
            {
                // `NONE` is the explicit dependency-free state, not an id that failed to resolve.
                // Counting it as unresolved would make the one declaration W1-07 requires look
                // like a dangling reference.
                if (DependencyDeclaration.IsExplicitNone(t)) continue;
                if (!edges.ContainsKey(t) && !unresolved.Contains(t, StringComparer.OrdinalIgnoreCase))
                    unresolved.Add(t);
            }

        var cycle = new List<string>();
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 0 unvisited, 1 in-stack, 2 done
        var stack = new List<string>();

        bool Visit(string node)
        {
            state[node] = 1;
            stack.Add(node);
            foreach (var next in edges.TryGetValue(node, out var t) ? t : Array.Empty<string>())
            {
                if (!edges.ContainsKey(next)) continue;
                if (!state.TryGetValue(next, out var s) || s == 0)
                {
                    if (Visit(next)) return true;
                }
                else if (s == 1)
                {
                    cycle.AddRange(stack.SkipWhile(x => !string.Equals(x, next, StringComparison.OrdinalIgnoreCase)));
                    cycle.Add(next);
                    return true;
                }
            }
            stack.RemoveAt(stack.Count - 1);
            state[node] = 2;
            return false;
        }

        foreach (var node in edges.Keys)
            if (!state.TryGetValue(node, out var s) || s == 0)
                if (Visit(node)) break;

        return (cycle, unresolved);
    }
}
