namespace Nexus.ProductCore.Contracts.DevelopmentControl;

/// <summary>
/// W8D FINAL TASK 3. The authority-marker sites, declared as data so that everything which must
/// agree about them can read one list.
///
/// <para><b>The problem this solves.</b> The V3 model does not carry the authority state in one
/// cell. It carries it in four, on four different sheets, and three of them are projections of the
/// one that actually governs. Measured on the 26-sheet candidate, every one of the four reads
/// <c>CANDIDATE</c>:</para>
/// <list type="table">
/// <item><description><c>00_Control!B10</c> — the marker itself. <c>AuthorizeWrite</c> and the
/// authority resolver read exactly this cell (`00_Control` row 10, item <c>ControlState</c>), so
/// this is the one that decides.</description></item>
/// <item><description><c>01_Configuration!D16</c> — configuration item <c>CFG-0012</c>, key
/// <c>control.state</c>, carrying the same value with the note "W3-24: creating the control does
/// not make it authoritative."</description></item>
/// <item><description><c>24_V3MigrationMap!R16</c> — the migration row for
/// <c>unified-control</c>, whose verification requirement reads "CONTROL_CUTOVER_GATE must pass
/// before this becomes authority".</description></item>
/// <item><description><c>25_Dashboard!B5</c> — the dashboard projection, declared as deriving from
/// <c>00_Control.ControlState</c> by its own neighbour cell <c>C5</c>.</description></item>
/// </list>
///
/// <para><b>Why the list is here and not in the writer.</b> Three separate things need it and they
/// must not be able to disagree: the writer, which refuses ordinary writes to these cells; the
/// cutover, which is the only thing permitted to write them; and a host, which has to be able to
/// report where the authority state lives without opening the OOXML itself. A list duplicated into
/// any one of those is a list that can drift from the other two, and the drift would be silent —
/// a cutover that flipped three of four sites would leave a workbook that reads
/// <c>AUTHORITATIVE</c> through the resolver and <c>CANDIDATE</c> on its own dashboard.</para>
///
/// <para><b>What is deliberately NOT here.</b> No XML, no column index, no part name. The physical
/// sheet and column are named because they are the fact being declared, but nothing here knows how
/// a workbook is opened. This file is in the contracts assembly, so Nexus.Developer can reference
/// it without referencing the implementation.</para>
/// </summary>
public static class DevelopmentControlAuthoritySites
{
    /// <summary>The value the model carries before cutover. Flipping this is the cutover act.</summary>
    public const string Candidate = "CANDIDATE";

    /// <summary>The value the model carries once it governs. `W3_SCHEMA.md:367` names the act.</summary>
    public const string Authoritative = "AUTHORITATIVE";

    /// <summary>
    /// One marker site. <paramref name="LogicalSheet"/> and <paramref name="LogicalColumn"/> are
    /// null when the sheet is not projected by the compatibility map, which is the case for
    /// <c>01_Configuration</c> and <c>25_Dashboard</c>. That null is load-bearing rather than
    /// cosmetic: it records that an ordinary logical cell write cannot reach this site at all,
    /// because the writer resolves a logical column before it resolves anything physical.
    /// </summary>
    public sealed record Site(
        string Role,
        string PhysicalSheet,
        string PhysicalColumn,
        int Row,
        string? LogicalSheet,
        string? LogicalColumn,
        string Meaning)
    {
        /// <summary>`00_Control!B10`, the form a diagnostic should print.</summary>
        public string Reference => $"{PhysicalSheet}!{PhysicalColumn}{Row}";

        /// <summary>True when the ordinary logical cell-write path can address this site.</summary>
        public bool ReachableByLogicalWrite => LogicalSheet is not null && LogicalColumn is not null;

        /// <summary>True when this site's declared value is a site at all — the shared predicate.</summary>
        public bool Matches(string physicalSheet, string physicalColumn, int row) =>
            Row == row
            && string.Equals(PhysicalSheet, physicalSheet, StringComparison.Ordinal)
            && string.Equals(PhysicalColumn, physicalColumn, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The four sites, in the order they are declared by the model: the marker that decides first,
    /// then its three carried projections.
    /// </summary>
    public static readonly IReadOnlyList<Site> All = new[]
    {
        new Site(
            "control-state",
            "00_Control", "B", 10,
            "Control", "Value",
            "The authority marker. `AuthorizeWrite` and `ResolveAuthority` read this cell and no "
            + "other, so it is the site that decides whether the control governs."),
        new Site(
            "config-item",
            "01_Configuration", "D", 16,
            null, null,
            "Configuration item CFG-0012 (`control.state`). It carries the same state with the note "
            + "\"W3-24: creating the control does not make it authoritative\", which is the rule the "
            + "cutover gate exists to honour."),
        new Site(
            "migration-status",
            "24_V3MigrationMap", "R", 16,
            "MigrationMap", "Status",
            "The `unified-control` migration row's status. Its verification requirement reads "
            + "\"CONTROL_CUTOVER_GATE must pass before this becomes authority\"."),
        new Site(
            "dashboard-projection",
            "25_Dashboard", "B", 5,
            null, null,
            "The dashboard's projection of the control state. Cell C5 of the same row declares it "
            + "derived from `00_Control.ControlState`, so leaving it behind would make the "
            + "authoritative workbook display a state it no longer holds."),
    };

    /// <summary>
    /// The sites an ordinary logical cell write could reach. Used to build the guard's message, and
    /// asserted by the tests so that a new site cannot be added into reach unnoticed.
    /// </summary>
    public static IReadOnlyList<Site> ReachableByLogicalWrite { get; } =
        All.Where(s => s.ReachableByLogicalWrite).ToArray();

    /// <summary>
    /// The physical-level predicate: is this exact cell one of the authority markers?
    ///
    /// <para>This is the form the WRITER uses, because the writer's last act before touching a
    /// cell is a physical one. A guard phrased in logical names would be enforced one layer up,
    /// where any future caller that resolves a physical column some other way would bypass it.</para>
    /// </summary>
    public static Site? FindPhysical(string physicalSheet, string physicalColumn, int row) =>
        All.FirstOrDefault(s => s.Matches(physicalSheet, physicalColumn, row));

    /// <summary>The guard's predicate in the vocabulary a logical caller speaks.</summary>
    public static Site? FindLogical(string logicalSheet, int row, string logicalColumn) =>
        All.FirstOrDefault(s =>
            s.LogicalSheet is not null && s.LogicalColumn is not null
            && s.Row == row
            && string.Equals(s.LogicalSheet, logicalSheet, StringComparison.Ordinal)
            && string.Equals(s.LogicalColumn, logicalColumn, StringComparison.Ordinal));

    /// <summary>
    /// Refusal text, shared so the writer's throw and the reservation's typed refusal cannot
    /// describe the same condition two different ways.
    /// </summary>
    public static string RefusalReason(Site site) =>
        $"GOVERNED_CELL — '{site.Reference}' is the {site.Role} authority site. {site.Meaning} "
        + "The authority state may not be changed by an ordinary write: promoting the control is a "
        + "governed cutover with its own preconditions, and a cell write that could perform it would "
        + "let any caller holding the lock promote the authority as a side effect of editing a "
        + "record. Use the explicit cutover operation.";
}
