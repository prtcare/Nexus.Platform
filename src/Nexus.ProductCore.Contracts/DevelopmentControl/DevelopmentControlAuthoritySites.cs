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
    /// W8D PRODUCTION WIRING. The IDENTITY cell of each site that has one: the cell in that site's
    /// row which NAMES the site, as against <see cref="All"/>, which holds the cells carrying its
    /// VALUE.
    ///
    /// <para><b>Why this is a second list and not two more entries in <see cref="All"/>.</b>
    /// <see cref="All"/> is what the cutover iterates, and it writes <c>toState</c> into every
    /// member. Putting an identity cell there would overwrite the item NAME with
    /// <c>AUTHORITATIVE</c> — the marker row would stop declaring <c>ControlState</c> and start
    /// declaring <c>AUTHORITATIVE</c>, which is both a promotion and the destruction of the row the
    /// promotion is read from. The two lists are consumed by different code and mean different
    /// things, so they are declared apart and neither can drift into the other.</para>
    ///
    /// <para><b>Why an identity cell needs a guard of its own.</b> The marker is not read from a
    /// fixed address; it is FOUND, by scanning <c>00_Control</c> for the row whose <c>ControlItem</c>
    /// column reads <c>ControlState</c> and taking that row's <c>Value</c>. So <c>B10</c> is the
    /// site, but <c>A10</c> is what makes the site findable at all. Guarding the value and not the
    /// name left a hole of exactly the same size: an ordinary governed write of <c>ControlStateRenamed</c>
    /// into <c>Control.ControlItem</c> at row 10 was allowed, and afterwards no row declared
    /// <c>ControlState</c> — the workbook reported its own authority as UNREADABLE rather than as
    /// wrong. Degradation rather than promotion, but still an ordinary write moving what the marker
    /// resolves to, which the directive forbids in either direction. Measured before and after; the
    /// probe's evidence line carries both readings.</para>
    ///
    /// <para><b>Only two of the four sites have one.</b> <c>01_Configuration</c> and
    /// <c>25_Dashboard</c> carry the state as a projection with no item column of their own, so they
    /// have no identity cell to rename. Their value cells remain guarded by <see cref="All"/>.</para>
    /// </summary>
    public static readonly IReadOnlyList<Site> IdentityCells = new[]
    {
        new Site(
            "control-state identity",
            "00_Control", "A", 10,
            "Control", "ControlItem",
            "This cell NAMES the authority marker. `ControlItems` locates the marker by the row whose "
            + "`ControlItem` reads `ControlState`, so renaming it does not change what the marker "
            + "says — it removes the marker from the workbook's vocabulary entirely."),
        new Site(
            "migration-status identity",
            "24_V3MigrationMap", "A", 16,
            "MigrationMap", "MapId",
            "This cell NAMES the `unified-control` migration row whose `Status` carries the migration "
            + "authority site. Renaming it leaves that status in the sheet but no longer attached to "
            + "a row anything can locate."),
    };

    /// <summary>
    /// The identity cell of the MARKER itself, and the only entry of <see cref="IdentityCells"/>
    /// whose item name the resolver searches for. Exposed separately because the guards that refuse
    /// the marker's item NAME — on the marker row and on any other — need to know which sheet and
    /// which column that name is reserved in, and re-deriving it from list order at each call site
    /// is how the two would come to disagree.
    /// </summary>
    public static Site MarkerIdentityCell => IdentityCells[0];

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
    /// The identity guard's physical predicate, consulted by the writer at the last point before a
    /// cell is touched — the same placement and the same reason as <see cref="FindPhysical"/>.
    /// Deliberately a separate method rather than a wider <see cref="FindPhysical"/>: the two answer
    /// different questions ("is this the cell that decides the state" and "is this the cell that
    /// names it") and a caller that could not tell them apart could not report which one stopped it.
    /// </summary>
    public static Site? FindPhysicalIdentity(string physicalSheet, string physicalColumn, int row) =>
        IdentityCells.FirstOrDefault(c => c.Matches(physicalSheet, physicalColumn, row));

    /// <summary>The identity guard's predicate in the vocabulary a logical caller speaks.</summary>
    public static Site? FindLogicalIdentity(string logicalSheet, int row, string logicalColumn) =>
        IdentityCells.FirstOrDefault(c =>
            c.LogicalSheet is not null && c.LogicalColumn is not null
            && c.Row == row
            && string.Equals(c.LogicalSheet, logicalSheet, StringComparison.Ordinal)
            && string.Equals(c.LogicalColumn, logicalColumn, StringComparison.Ordinal));

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

    /// <summary>
    /// Refusal text for an IDENTITY cell, kept separate from <see cref="RefusalReason"/> because it
    /// describes a different finding. Overwriting a site's value moves the state; renaming its
    /// identity makes the state UNREADABLE — the resolver stops finding the marker, and every
    /// consumer is told the authority cannot be established rather than that it changed. Collapsing
    /// the two into one message would make the component unable to say which of those it prevented,
    /// and the two call for different repairs. The leading token is shared so that the reservation's
    /// typed refusal still recognises it.
    /// </summary>
    public static string IdentityRefusalReason(Site cell) =>
        $"GOVERNED_CELL — '{cell.Reference}' is the {cell.Role} cell. {cell.Meaning} "
        + "Promoting or retiring the control is a governed cutover with its own preconditions, and an "
        + "ordinary write may neither move the authority state nor take away the row it is read from. "
        + "Use the explicit cutover operation.";

    /// <summary>
    /// Refusal text for a write that would put the marker's own ITEM NAME into a row that is not the
    /// marker row — a second declaration the resolver cannot tell from the first. Distinct from
    /// <see cref="IdentityRefusalReason"/> because no declared cell is being touched: the write
    /// targets an ordinary row of the identity column, and it is the VALUE that makes it a marker.
    /// </summary>
    public static string ReservedItemNameRefusalReason(string logicalSheet, string logicalColumn, string itemName) =>
        $"GOVERNED_CELL — '{logicalSheet}.{logicalColumn}' = '{itemName}' would create a second row "
        + $"naming the authority marker. The marker is located by that name and not by a fixed row, so "
        + $"the new row would stand alongside the one that carries the state and, because the "
        + $"projection resolves in row order, would decide it. Promoting or retiring the control is a "
        + $"governed cutover with its own preconditions; the marker's item name is reserved, and an "
        + $"ordinary write may not claim it. Use the explicit cutover operation.";
}
