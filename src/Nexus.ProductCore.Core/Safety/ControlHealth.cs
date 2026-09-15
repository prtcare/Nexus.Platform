// ControlHealth.cs — W1-08 TRUTHFUL CONTROL HEALTH.
//
// THE DEFECT THIS REPLACES
// ------------------------
// Forge's ControlHealthService (DevBridge.Engine\ControlHealthService.cs) builds a
// ControlHealth whose fields are COUNTS: OpenActiveChanges, OpenBlockers, OpenDecisions,
// OpenAuditFindings, with WorkbookState defaulting to "ERROR" and ExpectedSheets = 14. It
// answers "how many rows are in the sheet", which is a question that can always be answered,
// and never answers "is this control system actually working", which is the question a health
// surface exists to answer.
//
// The property V3_M1D_SAFETY_GAPS.md records as guard G-5 is sharper than "the metrics are
// weak": the counts cannot FAIL. There is no input to that evaluation which produces a
// non-green result, so a control store with a broken lock, an unrecognised schema, a stale
// lease and a dependency cycle reports exactly what a healthy one reports.
//
// WHAT THIS DOES INSTEAD
// ----------------------
// Every subcheck is a MEASUREMENT with three possible outcomes, one of which is UNKNOWN, and
// UNKNOWN is never green. The aggregate is DERIVED — it is not a field anybody sets — and the
// derivation is monotone: one BLOCKED subcheck makes the whole report BLOCKED regardless of
// how many other subchecks are healthy. There is no averaging, no threshold and no weighting,
// because every one of those is a way to make a failure disappear into a number.
namespace Nexus.DevelopmentControl.Safety;

public enum HealthStatus
{
    /// <summary>Not measured. NEVER green.</summary>
    Unknown = 0,

    /// <summary>Measured and passing.</summary>
    Healthy = 1,

    /// <summary>Measured and impaired. The system runs, but not correctly.</summary>
    Degraded = 2,

    /// <summary>Measured and unusable. The system must not be trusted to govern.</summary>
    Blocked = 3,
}

public sealed record HealthCheck(string Name, HealthStatus Status, string Detail, IReadOnlyList<string> Evidence)
{
    public static HealthCheck Ok(string name, string detail, params string[] evidence) =>
        new(name, HealthStatus.Healthy, detail, evidence);

    public static HealthCheck Bad(string name, HealthStatus status, string detail, params string[] evidence) =>
        new(name, status, detail, evidence);

    public static HealthCheck NotMeasured(string name, string detail, params string[] evidence) =>
        new(name, HealthStatus.Unknown, detail, evidence);
}

/// <summary>
/// The measured inputs. Everything is nullable on purpose: a subcheck whose input was not
/// supplied reports UNKNOWN, and UNKNOWN is never green. Making the inputs required would
/// force callers to fabricate them, which is how a health check starts lying.
/// </summary>
public sealed record ControlHealthInputs
{
    public bool? StoreReadable { get; init; }
    public string? StoreReadError { get; init; }

    public WorkbookReadResult? WorkbookSchema { get; init; }

    /// <summary>The lock subcheck's own measurement.</summary>
    public bool? LockSystemFunctioning { get; init; }
    public string? LockSystemDetail { get; init; }

    /// <summary>Every reservation the store currently holds, whatever its recorded state.</summary>
    public IReadOnlyList<ReservationLease>? Reservations { get; init; }

    /// <summary>Now, injected so the evaluation is deterministic.</summary>
    public DateTimeOffset? Now { get; init; }

    /// <summary>Dependency edges, for cycle and endpoint analysis.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? DependencyEdges { get; init; }

    /// <summary>Reservations whose scope ranges collide with one another.</summary>
    public IReadOnlyList<string>? ReservationConflicts { get; init; }

    /// <summary>Reservations that carry no BaseSHA.</summary>
    public IReadOnlyList<string>? ReservationsMissingBaseline { get; init; }

    /// <summary>Governed records that could not be bound to a repository baseline.</summary>
    public IReadOnlyList<string>? LineageGaps { get; init; }
}

public sealed record ControlHealthReport(
    HealthStatus Aggregate,
    IReadOnlyList<HealthCheck> Checks,
    string Summary)
{
    public bool IsGreen => Aggregate == HealthStatus.Healthy;

    /// <summary>The blockers, in full. A caller that reports only the aggregate is reporting a
    /// summary of a summary, and the point of this type is that the blockers stay visible.</summary>
    public IReadOnlyList<HealthCheck> Blockers =>
        Checks.Where(c => c.Status is HealthStatus.Blocked or HealthStatus.Degraded or HealthStatus.Unknown).ToArray();
}

public static class ControlHealthService
{
    public static ControlHealthReport Evaluate(ControlHealthInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var checks = new List<HealthCheck>();
        var now = inputs.Now ?? DateTimeOffset.UtcNow;

        // ---- 1. store readable
        checks.Add(inputs.StoreReadable switch
        {
            true => HealthCheck.Ok("store-readable", "The control store opened and parsed."),
            false => HealthCheck.Bad("store-readable", HealthStatus.Blocked,
                "The control store could not be read.",
                inputs.StoreReadError ?? "(no detail supplied)"),
            _ => HealthCheck.NotMeasured("store-readable", "Store readability was not measured."),
        });

        // ---- 2. schema recognised
        checks.Add(inputs.WorkbookSchema switch
        {
            null => HealthCheck.NotMeasured("schema-recognised", "The workbook schema was not measured."),
            { Result: ReaderResult.Supported } r => HealthCheck.Ok("schema-recognised",
                $"Recognised as {r.Form} with {r.Records.Count} records."),
            { Result: ReaderResult.PartiallySupported } r => HealthCheck.Bad("schema-recognised", HealthStatus.Degraded,
                $"Recognised as {r.Form} but only partially read ({r.Records.Count} records).", r.Diagnostics.ToArray()),
            { Result: ReaderResult.EmptyValid } r => HealthCheck.Bad("schema-recognised", HealthStatus.Degraded,
                $"Structurally valid {r.Form} workbook with no governed records. A store that governs nothing is not a healthy store.",
                r.Diagnostics.ToArray()),
            { Result: ReaderResult.UnsupportedSchema } r => HealthCheck.Bad("schema-recognised", HealthStatus.Blocked,
                "The workbook is structurally valid but its schema is not recognised.", r.Diagnostics.ToArray()),
            { Result: ReaderResult.Corrupt } r => HealthCheck.Bad("schema-recognised", HealthStatus.Blocked,
                "The workbook is not a readable workbook.", r.Diagnostics.ToArray()),
            var r => HealthCheck.NotMeasured("schema-recognised", $"Unhandled reader result {r.Result}."),
        });

        // ---- 3. lock system functioning
        checks.Add(inputs.LockSystemFunctioning switch
        {
            true => HealthCheck.Ok("lock-system", inputs.LockSystemDetail ?? "The writer lock acquired and released."),
            false => HealthCheck.Bad("lock-system", HealthStatus.Blocked,
                "The writer lock did not function.", inputs.LockSystemDetail ?? "(no detail supplied)"),
            _ => HealthCheck.NotMeasured("lock-system", "The lock system was not exercised."),
        });

        // ---- 4. stale leases  (W1-03's state, surfaced)
        if (inputs.Reservations is null)
        {
            checks.Add(HealthCheck.NotMeasured("stale-leases", "Reservations were not read, so staleness cannot be reported."));
        }
        else
        {
            var stale = inputs.Reservations.Where(r => r.StatusAt(now) == ReservationStatus.Stale).ToArray();
            var expired = inputs.Reservations.Where(r => r.StatusAt(now) == ReservationStatus.Expired).ToArray();
            var detail = $"{inputs.Reservations.Count} reservation(s): {stale.Length} stale, {expired.Length} expired.";
            checks.Add(expired.Length > 0
                ? HealthCheck.Bad("stale-leases", HealthStatus.Degraded, detail,
                    expired.Select(r => $"expired {r.ReservationId} holder={r.WorkerId} expiry={r.Expiry:o}").ToArray())
                : stale.Length > 0
                    ? HealthCheck.Bad("stale-leases", HealthStatus.Degraded, detail,
                        stale.Select(r => $"stale {r.ReservationId} lastHeartbeat={r.LastHeartbeat:o}").ToArray())
                    : HealthCheck.Ok("stale-leases", detail));
        }

        // ---- 5. reservation conflicts
        checks.Add(inputs.ReservationConflicts switch
        {
            null => HealthCheck.NotMeasured("reservation-conflicts", "Collision between active reservations was not computed."),
            { Count: > 0 } c => HealthCheck.Bad("reservation-conflicts", HealthStatus.Blocked,
                $"{c.Count} active reservation pair(s) collide.", c.ToArray()),
            _ => HealthCheck.Ok("reservation-conflicts", "No two active reservations collide."),
        });

        // ---- 6 & 7. dependency integrity: endpoints and cycles
        if (inputs.DependencyEdges is null)
        {
            checks.Add(HealthCheck.NotMeasured("dependency-integrity", "The dependency graph was not supplied."));
            checks.Add(HealthCheck.NotMeasured("dependency-endpoints", "The dependency graph was not supplied."));
            checks.Add(HealthCheck.NotMeasured("dependency-cycles", "The dependency graph was not supplied."));
        }
        else
        {
            var (cycle, unresolved) = ReadinessGate.AnalyseGraph(inputs.DependencyEdges);

            checks.Add(unresolved.Count > 0
                ? HealthCheck.Bad("dependency-endpoints", HealthStatus.Degraded,
                    $"{unresolved.Count} dependency endpoint(s) resolve to no work item.", unresolved.ToArray())
                : HealthCheck.Ok("dependency-endpoints",
                    $"{inputs.DependencyEdges.Count} node(s), every declared endpoint resolves."));

            checks.Add(cycle.Count > 0
                ? HealthCheck.Bad("dependency-cycles", HealthStatus.Blocked,
                    "The dependency graph contains a cycle, so no member of it can ever become ready.",
                    string.Join(" -> ", cycle))
                : HealthCheck.Ok("dependency-cycles", "The dependency graph is acyclic."));

            // Blank dependency declarations are W1-07's defect and they belong in health.
            // Reported here as DEGRADED, not BLOCKED: legacy rows are not rewritten in W1,
            // so their presence is expected and must be visible rather than alarming.
            var blanks = inputs.DependencyEdges.Count(kv => kv.Value.Count == 0);
            checks.Add(blanks > 0
                ? HealthCheck.Bad("dependency-integrity", HealthStatus.Degraded,
                    $"{blanks} work item(s) declare no dependencies at all. A blank declaration is UNKNOWN, not dependency-free; " +
                    "these rows must be explicitly resolved before they can become READY.")
                : HealthCheck.Ok("dependency-integrity", "Every work item carries an explicit dependency declaration."));
        }

        // ---- 8. compatibility reader status
        checks.Add(inputs.WorkbookSchema is null
            ? HealthCheck.NotMeasured("compatibility-reader", "The compatibility reader was not run.")
            : inputs.WorkbookSchema.Result is ReaderResult.Supported or ReaderResult.PartiallySupported
                ? HealthCheck.Ok("compatibility-reader",
                    $"The reader recognised the {inputs.WorkbookSchema.Form} family and reported {inputs.WorkbookSchema.Result}.")
                : HealthCheck.Bad("compatibility-reader", HealthStatus.Degraded,
                    $"The reader did not fully recognise the workbook: {inputs.WorkbookSchema.Result}.",
                    inputs.WorkbookSchema.Diagnostics.ToArray()));

        // ---- 9. lineage / baseline completeness
        if (inputs.ReservationsMissingBaseline is null && inputs.LineageGaps is null)
        {
            checks.Add(HealthCheck.NotMeasured("lineage-completeness",
                "Baseline and lineage completeness were not measured."));
        }
        else
        {
            var missing = inputs.ReservationsMissingBaseline ?? Array.Empty<string>();
            var gaps = inputs.LineageGaps ?? Array.Empty<string>();
            checks.Add(missing.Count > 0 || gaps.Count > 0
                ? HealthCheck.Bad("lineage-completeness", HealthStatus.Degraded,
                    $"{missing.Count} reservation(s) carry no BaseSHA and {gaps.Count} record(s) cannot be bound to a baseline. " +
                    "Work to commit lineage is not reconstructable for these.",
                    missing.Concat(gaps).ToArray())
                : HealthCheck.Ok("lineage-completeness", "Every reservation carries a BaseSHA and every governed record is bound."));
        }

        // ---- the aggregate. DERIVED, monotone, and never green over a failure.
        var aggregate = Derive(checks);
        var summary = BuildSummary(aggregate, checks);

        return new ControlHealthReport(aggregate, checks, summary);
    }

    /// <summary>
    /// The derivation. Worst subcheck wins; there is no arithmetic that can dilute a failure.
    /// UNKNOWN ranks above HEALTHY for the same reason: not having measured something is not
    /// the same as having measured it and found it good.
    /// </summary>
    public static HealthStatus Derive(IReadOnlyList<HealthCheck> checks)
    {
        if (checks.Count == 0) return HealthStatus.Unknown;
        if (checks.Any(c => c.Status == HealthStatus.Blocked)) return HealthStatus.Blocked;
        if (checks.Any(c => c.Status == HealthStatus.Degraded)) return HealthStatus.Degraded;
        if (checks.Any(c => c.Status == HealthStatus.Unknown)) return HealthStatus.Unknown;
        return HealthStatus.Healthy;
    }

    private static string BuildSummary(HealthStatus aggregate, IReadOnlyList<HealthCheck> checks)
    {
        var blocked = checks.Where(c => c.Status == HealthStatus.Blocked).Select(c => c.Name).ToArray();
        var degraded = checks.Where(c => c.Status == HealthStatus.Degraded).Select(c => c.Name).ToArray();
        var unknown = checks.Where(c => c.Status == HealthStatus.Unknown).Select(c => c.Name).ToArray();

        return aggregate switch
        {
            HealthStatus.Healthy =>
                $"HEALTHY — all {checks.Count} subchecks measured and passing.",
            HealthStatus.Blocked =>
                $"BLOCKED — {blocked.Length} blocking subcheck(s): {string.Join(", ", blocked)}.",
            HealthStatus.Degraded =>
                $"DEGRADED — {degraded.Length} impaired subcheck(s): {string.Join(", ", degraded)}." +
                (blocked.Length > 0 ? $" ({blocked.Length} blocking)" : ""),
            _ =>
                $"UNKNOWN — {unknown.Length} subcheck(s) were not measured: {string.Join(", ", unknown)}. " +
                "Not-measured is not the same as healthy.",
        };
    }
}
