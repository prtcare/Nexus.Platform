// WorkbookCompatibilityReader.cs — W1-09 THREE-FAMILY WORKBOOK COMPATIBILITY READER.
//
// WHAT THIS HAS TO PROVE
// ----------------------
// V3_M1D_WORKBOOK_COMPATIBILITY.md §7 answers the question "can one adapter read all three
// forms unchanged?" with YES, under four conditions A-1..A-4, and §8 turns those into WC-1..WC-8.
// This file is those eight rules made executable:
//
//   WC-1  sheet binding is (Form, LogicalName) -> (PhysicalName, HeaderRow, DataStart, DataEnd)
//   WC-2  columns resolve by NAME inside the located header row; index access is prohibited
//   WC-3  both t="s" and t="inlineStr" decode; an undecodable cell RAISES, never returns empty
//   WC-4  the three forms project to one logical model
//   WC-5  a logical sheet with no counterpart in a form reports ABSENT_IN_FORM
//   WC-6  a missing Version History / Session Protocol / Activity Log is
//         GOVERNANCE_SUBSTRATE_ABSENT and blocks that form from governing
//   WC-7  source workbooks open read-only; this type has no write path at all
//   WC-8  the three frozen forms are identified by hash
//
// THE TWO TRAPS THIS EXISTS TO CLOSE
// ----------------------------------
// 1. THE SILENT-BLANK TRAP (§1). Legacy carries 6 388 shared strings; both V2 forms carry ZERO
//    and put the text directly in the cell — the t="str" form, verified against the shipped
//    packages rather than assumed (t="inlineStr" alone would have reported zero). A reader that
//    understands only t="s" reads every V2 string cell as EMPTY — no exception, no warning, just
//    a workbook that appears to govern nothing. This is the most dangerous compatibility defect
//    in the task precisely because its failure mode is a blank cell.
//
// 2. THE COLUMN-POSITION TRAP (§4). `DependsOn` is column H in Foundation `04_Tasks` and column
//    I in Products `03_Tasks` — two workbooks that each declare themselves to be the "same
//    logical schema". Any positional read returns a plausible value from the adjacent column
//    and never throws. This reader resolves by name, and it treats a REQUIRED logical column it
//    cannot find as a failure of recognition — so a wrong sheet or a wrong header row produces
//    UNSUPPORTED_SCHEMA rather than a confident, wrong answer.
//
// And the structural guarantee the directive names explicitly: a recognised workbook NEVER
// returns success with zero records. Zero governed records in a recognised, well-formed workbook
// is EMPTY_VALID — a distinct, non-success outcome that names what was found.
//
// The reader is zero-dependency on purpose (System.IO.Compression + System.Xml.Linq only), so a
// BOOTSTRAP_SAFE caller can read the store with no Nexus service, no feed and no NuGet.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Nexus.DevelopmentControl.Safety;

public enum WorkbookForm
{
    Unknown = 0,
    LegacyL2 = 1,
    V2Foundation = 2,
    V2Products = 3,

    /// <summary>
    /// W8D-R TASK 4. The 26-sheet V3 model. This is a FOURTH member of an existing enum on an
    /// existing type — it is not a second reader. Everything below it (the hash check, the sheet
    /// inventory, the named column resolution, the five-valued verdict, the decode accounting)
    /// is the same code the three frozen forms already run through.
    ///
    /// V3 is NOT a frozen reference. The other three forms are identified by a pinned SHA-256
    /// because they are closed; V3 is the live candidate that is expected to change, so it has
    /// no frozen hash and <see cref="WorkbookCompatibilityMap.FrozenHashFor"/> returns null for
    /// it. Recording a pin here would make every legitimate V3 edit look like a divergence.
    /// </summary>
    V3 = 4,
}

/// <summary>The five outcomes. <see cref="EmptyValid"/> exists so that "recognised but empty" is
/// never collapsed into "supported".</summary>
public enum ReaderResult
{
    Supported = 0,
    PartiallySupported = 1,
    UnsupportedSchema = 2,
    Corrupt = 3,
    EmptyValid = 4,
}

/// <summary>WC-5. <see cref="AbsentInForm"/> is EXPECTED for a logical sheet that a form has no
/// counterpart for; <see cref="DeclaredButMissing"/> is a schema mismatch, because the map said
/// the sheet would be there.</summary>
public enum SheetPresence
{
    Bound = 0,
    AbsentInForm = 1,
    DeclaredButMissing = 2,
    Unreadable = 3,
}

public sealed record SheetBinding(
    string LogicalName,
    string? LegacyName,
    string? FoundationName,
    string? ProductsName,
    int? LegacyHeaderRow,
    int? FoundationHeaderRow,
    int? ProductsHeaderRow,
    /// <summary>WC-6: absence of this sheet from a form blocks that form from governing.</summary>
    bool GovernanceSubstrate = false,
    /// <summary>Sheets not in the document's governance gap list but still legacy-only.</summary>
    bool LegacyOnly = false,
    /// <summary>
    /// W8D-R TASK 4. The physical sheet name in the 26-sheet V3 model, or null where the V3
    /// model has no counterpart for this logical sheet. Null is <see cref="SheetPresence.AbsentInForm"/>,
    /// never "empty" — the same distinction the other three forms already make.
    /// </summary>
    string? V3Name = null);

public sealed record ColumnBinding(
    string LogicalSheet,
    string LogicalName,
    bool Required,
    string? Legacy = null,
    string? Foundation = null,
    string? Products = null,
    /// <summary>W8D-R TASK 4. The V3 header for this logical column, or null where the V3 model
    /// carries no such column. Null is absent-in-form, which the collision gate must read as
    /// "scope not readable" and never as "no scope declared" (W1-04).</summary>
    string? V3 = null,
    /// <summary>
    /// W8D-R TASK 4. Which occurrence of the header NAME to bind, 1-based.
    ///
    /// This exists because five V3 sheets (`10_Changes`, `11_Reservations`, `12_DevelopmentRuns`,
    /// `13_GitLineage`, `17_Evidence`) carry the literal header `ChangeId` TWICE: once as the
    /// business key of the record, and once as the envelope's originating-change field. A
    /// name-only lookup binds whichever occurrence happens to enumerate first and never fails —
    /// which is precisely the "read a plausible value out of the wrong column" failure that
    /// resolving by name (WC-2) was introduced to prevent. The occurrence is therefore declared,
    /// not inferred.
    /// </summary>
    int V3Occurrence = 1,
    /// <summary>
    /// W8D-R TASK 4. Per-form override of <paramref name="Required"/>. Null means "inherit".
    ///
    /// Exactly one column needs this. `WorkGraph.DependsOn` is required on all three frozen
    /// forms because there the work-item row carries its own dependency list. V3 normalised
    /// dependencies out of the row and into a typed edge table (`08_Dependencies`), which the
    /// `Dependencies` logical sheet already binds. Demanding the column anyway would make every
    /// V3 read return UNSUPPORTED_SCHEMA — the reader would be rejecting the model for having
    /// been normalised, which is not a schema mismatch.
    /// </summary>
    bool? V3Required = null);

public sealed record ColumnResolution(string LogicalName, string? PhysicalName, bool Found, bool Required);

public sealed record WorkbookRecord(string LogicalSheet, int Row, IReadOnlyDictionary<string, string> Values)
{
    public string? Get(string logicalColumn) =>
        Values.TryGetValue(logicalColumn, out var v) ? v : null;
}

public sealed record SheetRead(
    string LogicalName,
    SheetPresence Presence,
    string? PhysicalName,
    int? HeaderRow,
    bool HeaderRowLocated,
    IReadOnlyList<ColumnResolution> Columns,
    IReadOnlyList<WorkbookRecord> Records,
    IReadOnlyList<string> DecodeFailures,
    IReadOnlyList<string> Diagnostics,
    int LastRow)
{
    public bool HasAllRequiredColumns => Columns.Where(c => c.Required).All(c => c.Found);
    public IReadOnlyList<string> MissingRequiredColumns =>
        Columns.Where(c => c.Required && !c.Found).Select(c => c.LogicalName).ToArray();
}

public sealed record WorkbookReadResult(
    ReaderResult Result,
    WorkbookForm Form,
    string SourcePath,
    string SourceSha256,
    bool MatchesFrozenHash,
    IReadOnlyList<SheetRead> Sheets,
    IReadOnlyList<string> Diagnostics,
    int SharedStringCount,
    int LiteralStringCount,
    IReadOnlyList<string> UnboundSheets,
    IReadOnlyList<string> GovernanceSubstrateAbsent)
{
    public IReadOnlyList<WorkbookRecord> Records => Sheets.SelectMany(s => s.Records).ToArray();
    public int DecodeFailureCount => Sheets.Sum(s => s.DecodeFailures.Count);
    public int RecordCount => Sheets.Sum(s => s.Records.Count);

    public SheetRead? Sheet(string logicalName) =>
        Sheets.FirstOrDefault(s => s.LogicalName == logicalName);

    /// <summary>WC-6. A form that cannot carry its own governance cannot be a governing authority.</summary>
    public bool MayGovern => Result is ReaderResult.Supported or ReaderResult.PartiallySupported
                             && GovernanceSubstrateAbsent.Count == 0;
}

/// <summary>
/// W8D-R2 TASK 10's answer, and deliberately not a path: `Available=false` carries the canonical
/// path that was EXPECTED, so a caller that logs the refusal also logs what it should have found.
/// There is no member holding a substitute path, because there is no substitute.
/// </summary>
public sealed record AuthorityResolution(bool Available, string Path, string ExpectedPath, string Reason);

/// <summary>WC-3. Raised when a cell cannot be decoded. Never swallowed into an empty value.</summary>
public sealed class WorkbookDecodeException : Exception
{
    public WorkbookDecodeException(string message) : base(message) { }
}

public static class WorkbookCompatibilityMap
{
    // ---- WC-8: the three frozen forms, by hash (V3_M1D_WORKBOOK_COMPATIBILITY.md §1).
    public const string LegacyFrozenSha256 = "8AA73778A3F1CDB97D1A58BB05BD4797CA5A409BE8F986EF19FD9EDBB7CFE69F";
    public const string FoundationFrozenSha256 = "4E74F00E8E4FF1A7B64111BEF2C538AAAC2AA2417F719ED8ED6FBD2281DBC6BC";
    public const string ProductsFrozenSha256 = "B645B0E9E33CFEF1389A971E3D9165C43E6F4C0CA0F4833A405703DDAF253CD6";

    // ---------------------------------------------------------------- W8D-R2 — the canonical paths
    //
    // Three paths, declared once. The first is the authority; the second is preserved and read-only;
    // the third is not a Nexus root at all.
    //
    // On the third: `C:\Personal` is named HERE only so that a refusal can say which substitute was
    // declined. Nothing in this assembly reads, writes, probes or falls back to it. Naming a path in
    // order to refuse it is not a dependency on it — and a guard that cannot name what it refuses
    // cannot be shown to refuse it.

    /// <summary>The canonical V3 authority. TASK 2 measured it present at 352,387 B / 3E29742C….</summary>
    public const string CanonicalAuthorityPath = @"D:\NEXUS\DevelopmentControl\NEXUS_DEVELOPMENT_CONTROL.xlsx";

    /// <summary>
    /// The 14-sheet workbook. PRESERVED PERMANENTLY, and designated readonly by the Owner's decision.
    /// Its own bytes still say `SourceWorkbook` on 1,728 provenance cells and that is correct — this
    /// constant names the FILE, not the historical provenance that records where a record came from.
    /// A guard that refused the string would refuse the audit trail with it.
    /// </summary>
    public const string LegacyAuthorityPath = @"D:\NEXUS\Products\Developer\NEXUS_DEVELOPMENT_CONTROL.xlsx";

    /// <summary>The retired root. Refused as a source of authority, in both directions.</summary>
    public const string PersonalRootPrefix = @"C:\Personal";

    /// <summary>WC-8. Three frozen forms are identified by a pinned SHA-256; V3 deliberately
    /// has none — it is the live candidate, expected to change, and a pin over it would make
    /// every legitimate edit read as a divergence. Null means "no frozen reference exists",
    /// which is why <see cref="WorkbookReadResult.MatchesFrozenHash"/> is false for V3 by
    /// construction rather than by failure.</summary>
    public static string? FrozenHashFor(WorkbookForm form) => form switch
    {
        WorkbookForm.LegacyL2 => LegacyFrozenSha256,
        WorkbookForm.V2Foundation => FoundationFrozenSha256,
        WorkbookForm.V2Products => ProductsFrozenSha256,
        WorkbookForm.V3 => null,
        _ => null,
    };

    /// <summary>WC-1. Header rows are the MEASURED values of §5.1, not assumptions:
    /// null for 1 sheet, row 5 for 5 legacy sheets, row 4 for the remaining 8 — and row 4
    /// uniformly across every sheet of both V2 forms.</summary>
    /// <remarks>
    /// W8D-R TASK 4 adds the V3 column. Every V3 sheet carries its header at row 4, measured
    /// across all 26 sheets, so it is recorded once as <see cref="V3HeaderRow"/> rather than
    /// repeated fourteen times — a repetition would read as fourteen independent measurements
    /// and it is one. <see cref="SheetBinding.V3Name"/> stays per-sheet because the names are
    /// genuinely per-sheet.
    ///
    /// Where V3 has no counterpart the V3Name is left null, which is ABSENT_IN_FORM — a named,
    /// reported outcome. Three of those are deliberate consequences of the V3 model rather than
    /// gaps, and are called out on the rows themselves.
    /// </remarks>
    public static readonly IReadOnlyList<SheetBinding> Sheets = new SheetBinding[]
    {
        // logical                legacy                      foundation        products          L.hdr F.hdr P.hdr  gov  legacyOnly
        new("Control",            "Control Center",           "00_Control",     "00_Control",     null, 4,    4)
            { V3Name = "00_Control" },
        new("WorkGraph",          "Master Roadmap",           "04_Tasks",       "03_Tasks",       5,    4,    4)
            { V3Name = "07_WorkItems" },
        // V3 folds milestones into `WorkType` on the work-item sheet — the sheet's own subtitle
        // says so: "Every governed unit of work. MILESTONES folded in via WorkType." There is no
        // V3 milestone sheet to bind, and inventing one would be fabricating a surface.
        new("Milestones",         "Phase Plan",               "03_Milestones",  "02_Milestones",  4,    4,    4),
        new("Changes",            "Active Changes",           "12_Changes",     "09_Changes",     5,    4,    4)
            { V3Name = "10_Changes" },
        new("Decisions",          "Open Decisions",           "11_Decisions",   "08_Decisions",   4,    4,    4)
            { V3Name = "20_Decisions" },
        new("Architecture",       "Architecture Decisions",   "02_Architecture", null,            4,    4,    null,  false, true)
            { V3Name = "02_Architecture" },
        new("Dependencies",       "Dependencies & Blockers",  "05_Dependencies", "05_Dependencies", 4,   4,    4)
            { V3Name = "08_Dependencies" },
        // V3 dissolves the separate version-history sheet: versioning moved IN-ROW, into the
        // envelope every governed sheet carries (RecordVersion | IsCurrent | EffectiveFrom |
        // ChangeId | SupersedesVersion). AbsentInForm is therefore the truthful reading of
        // "this form has no Version History sheet" — but see V3GovernanceSubstrateDuties for
        // what that does and does not say about whether V3 may govern.
        new("VersionHistory",     "Version History",          null,             null,             5,    null, null,  true,  true),
        new("SessionProtocol",    "Session Protocol",         null,             null,             5,    null, null,  true,  true),
        new("ActivityLog",        "Activity Log",             null,             null,             4,    null, null,  true,  true),
        new("AuditFindings",      "Audit Findings",           null,             null,             5,    null, null,  false, true),
        new("DevelopmentGuide",   "Development Guide",        null,             null,             4,    null, null,  false, true),
        new("ExistingAssets",     "Existing Assets",          "15_Repositories", "12_Repositories", 4,  4,    4)
            { V3Name = "06_Repositories" },
        new("ToolRegistry",       "Tool & Integration Registry", null,           null,             4,    null, null,  false, true),
    };

    /// <summary>
    /// W8D-R TASK 4. WC-1's measurement for the V3 model: the header row is row 4 on every one
    /// of its 26 sheets. Measured by reading each sheet's true row numbers from the OOXML `r`
    /// attributes — not by counting rows, because V3 omits no row elements while the legacy
    /// workbook omits exactly one (row 4, a spacer) above its headers, so an index-based
    /// measurement silently shifts the legacy header from row 5 to row 4.
    /// </summary>
    public const int V3HeaderRow = 4;

    /// <summary>
    /// WC-2. Every column a caller may consume is named here per form. The two entries that
    /// carry the whole weight of §4 are WorkGraph.DependsOn (legacy `Dependencies`,
    /// Foundation `DependsOn`, Products `DependsOn` — H in one V2 workbook, I in the other) and
    /// WorkGraph.ParallelLane (legacy's single `Parallel Safe` flag vs V2's `ParallelLane`).
    /// Where a form has no measured header for a column the entry is null: the column is simply
    /// not part of that form's projection, and W1 does not invent a name for it.
    /// </summary>
    public static readonly IReadOnlyList<ColumnBinding> Columns = new ColumnBinding[]
    {
        // Control — §3 row 13. Legacy writes its changelog at A2 and a dashboard block at row 4;
        // V2 uses a key/value block under a `Control Item` / `Value` header at row 4. The
        // headerless legacy shape is handled by the generic fallback below, so only the V2
        // spelling is bound here.
        new("Control", "ControlItem",       false, null, "Control Item", "Control Item")
            { V3 = "ControlItem" },
        new("Control", "Value",             false, null, "Value",        "Value")
            { V3 = "Value" },

        // WorkGraph — the work-item sheet. `WorkId` and `DependsOn` are REQUIRED: they are what
        // makes a wrong sheet or a wrong header row fail loudly instead of quietly.
        new("WorkGraph", "WorkId",          true,  "Node ID",     "WorkId",       "WorkId")
            { V3 = "WorkId" },
        // V3Required=false, and the reason is a model change rather than a relaxation: V3 moved
        // dependency edges OUT of the work-item row and into the typed `08_Dependencies` table,
        // which the `Dependencies` logical sheet above already binds on every form. The legacy
        // and V2 forms genuinely carry the list in the row; V3 genuinely does not. Leaving the
        // requirement in place would reject V3 with UNSUPPORTED_SCHEMA for having been
        // normalised, which is not what "required column missing" is for.
        new("WorkGraph", "DependsOn",       true,  "Dependencies","DependsOn",    "DependsOn")
            { V3Required = false },
        new("WorkGraph", "Title",           false, "Name",        "WorkItem",     "WorkItem")
            { V3 = "Title" },
        new("WorkGraph", "ParentWorkId",    false, "Parent ID",   "ParentWorkId", "ParentWorkId")
            { V3 = "ParentWorkId" },
        new("WorkGraph", "ParallelLane",    false, "Parallel Safe","ParallelLane","ParallelLane")
            { V3 = "ParallelLane" },
        // OPEN ADAPTATION ITEM, recorded rather than silently resolved: V3 splits what all three
        // frozen forms carry as ONE `Status` column into three — `ReadinessState` (the gate),
        // `ExecutionStatus` (progress) and `LegacyStatusText` (the pre-migration wording). This
        // map has one logical `Status`, and it binds the V3-native progress field. Whether the
        // readiness gate should instead consume `ReadinessState` is a semantic decision for the
        // Owner, not a reader decision, so it is bound to the natural continuation and flagged.
        new("WorkGraph", "Status",          false, "Status",      "Status",       "Status")
            { V3 = "ExecutionStatus" },
        // V3 carries neither of these on the work-item sheet: file and contract scope moved to
        // the normalised `09_ChangeScopes` table (WorkId | ScopeClass | ItemType | Target |
        // Access | ReviewState), which is a different shape and a different cardinality.
        // Binding nothing here is deliberate — Found=false is "scope not readable", which the
        // collision gate must not read as "no scope declared" (W1-04). Extending the scope
        // projection to consume `09_ChangeScopes` is a ChangeScope-engine change, not a reader
        // change, and it is NOT performed here.
        new("WorkGraph", "FilesGlobs",      false, "Files / Globs"),
        new("WorkGraph", "ContractsApis",   false, "Contracts / APIs"),

        // Milestones — §3 row 1: the legacy roadmap is SPLIT in two in V2, so this logical sheet
        // carries the sequencing half. Legacy `Phase Plan` is rank-only and never a gate (M1C
        // ORDER_ONLY), which is why it binds no columns here.
        new("Milestones", "MilestoneId",    false, null,          "MilestoneId",  "MilestoneId"),
        new("Milestones", "ParallelLane",   false, null,          "ParallelLane", "ParallelLane"),
        new("Milestones", "ParallelStrategy", false, null,        "ParallelStrategy", "ParallelStrategy"),
        new("Milestones", "Status",         false, null,          "Status",       "Status"),

        // Dependencies — §3 row 3: legacy is a 10-column RELATION sheet, V2 is a typed edge
        // table. Different shapes, one logical projection.
        new("Dependencies", "SourceId",     true,  "From Node",            "SourceId",  "SourceId")
            { V3 = "FromWorkId" },
        new("Dependencies", "TargetId",     true,  "Depends On / Blocks",  "TargetId",  "TargetId")
            { V3 = "ToWorkId" },
        new("Dependencies", "RelationType", false, "Relation Type",        "DependencyType", "DependencyType")
            { V3 = "DependencyType" },
        new("Dependencies", "Status",       false, "Status",               "Status",    "Status")
            { V3 = "Status" },

        // Changes — the RESERVATION LEDGER. W1 Closure added the legacy bindings below.
        //
        // Why they did not exist before: the reader's first job (W1-09) was to READ all three
        // families for compatibility, and `ChangeId`/`Status` were enough for a compatibility
        // verdict. They are NOT enough for the live collision gate, which must intersect the
        // candidate's declared scope against what each open reservation actually declared. The
        // legacy form carries that scope in named columns (verified against the shipped
        // workbook: A=Change ID, G=Repositories, H=Projects, I=Files / Globs,
        // K=Contracts / APIs, L=Status, AB=Affected Nodes), so the projection is a binding
        // change, not a new reader.
        //
        // The two V2 forms leave these null: their reservation scope is not in these columns.
        // A null binding is ABSENT_IN_FORM — the column is not part of that form's projection —
        // which the collision gate must read as "scope not readable", never as "no scope
        // declared". That distinction is the W1-04 rule and it is load-bearing here: a
        // reservation whose scope cannot be read must not silently pass a collision check.
        // V3Occurrence = 1 is stated rather than implied: `10_Changes` declares the header
        // `ChangeId` twice — column A is the change's own key, column S is the envelope's
        // originating change. Without the occurrence the binding is whichever the dictionary
        // enumerates first, which is not a contract.
        new("Changes", "ChangeId",          false, "Change ID",    "ChangeId",     "ChangeId")
            { V3 = "ChangeId", V3Occurrence = 1 },
        new("Changes", "Title",             false, null, "Title",        "Title")
            { V3 = "Title" },
        new("Changes", "Status",            false, "Status",       "Status",       "Status")
            { V3 = "Status" },
        new("Changes", "ImplementationWorkId", false, "Node ID",  "ImplementationWorkId", "ImplementationWorkId")
            { V3 = "ImplementationWorkId" },
        new("Changes", "Repositories",      false, "Repositories"),
        new("Changes", "Projects",          false, "Projects"),
        new("Changes", "FilesGlobs",        false, "Files / Globs"),
        new("Changes", "ContractsApis",     false, "Contracts / APIs"),
        new("Changes", "AffectedNodes",     false, "Affected Nodes"),
        new("Changes", "Branch",            false, "Branch"),
        new("Changes", "Worktree",          false, "Worktree"),

        new("Decisions", "DecisionId",      false, null, "DecisionId",   "DecisionId")
            { V3 = "DecisionId" },
        new("Decisions", "Title",           false, null, "Title",        "Title")
            { V3 = "Title" },
        new("Decisions", "Status",          false, null, "Status",       "Status")
            { V3 = "Status" },

        new("Architecture", "EntityId",     false, null, "EntityId")
            { V3 = "EntityId" },
        new("Architecture", "Name",         false, null, "Name")
            { V3 = "Name" },
        new("Architecture", "Status",       false, null, "Status")
            { V3 = "Status" },

        // The governance substrate. Measured coverage only — Version History's header row 5 was
        // read directly (A..AJ), and it is the sheet that makes ADR-003 satisfiable.
        new("VersionHistory", "NodeId",     true,  "Node ID", null, null),
        new("VersionHistory", "Status",     false, "Status",  null, null),
        new("VersionHistory", "ChangeId",   false, "Change ID", null, null),
        new("VersionHistory", "IsCurrent",  false, "Is Current", null, null),

        new("SessionProtocol", "Step",      true,  "Step", null, null),
        new("SessionProtocol", "Action",    false, "Required action", null, null),
        new("SessionProtocol", "StopCondition", false, "Stop condition", null, null),

        new("ActivityLog", "ActivityId",    true,  "Activity ID", null, null),
        new("ActivityLog", "TimestampUtc",  false, "Timestamp UTC", null, null),
        new("ActivityLog", "Operation",     false, "Operation", null, null),
        new("ActivityLog", "ChangeId",      false, "Change ID", null, null),
        new("ActivityLog", "Result",        false, "Result", null, null),
        // These three carry exactly the facts W1-05's collision engine consumes.
        new("ActivityLog", "Repository",    false, "Repository", null, null),
        new("ActivityLog", "Branch",        false, "Branch", null, null),
        new("ActivityLog", "Worktree",      false, "Worktree", null, null),
        new("ActivityLog", "FilesGlobs",    false, "Files/Globs", null, null),
    };

    /// <summary>
    /// WC-5's other half — sheets present in a workbook that no logical binding consumes. They
    /// are reported by name so that "nothing was silently ignored" is a checkable claim. Both
    /// V2 forms carry 9–12 of these (§3 rows 15 and 16); legacy carries none.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownUnboundSheets = new[]
    {
        // Foundation-only (§2.2 minus the bound ones)
        "01_Scopes", "06_Capabilities", "07_Contracts", "08_ParallelPlan", "09_ForgeAdaptation",
        "10_Adaptability", "13_Evidence", "14_Releases", "16_Risks", "17_Dashboard",
        // Products-only (§2.3 minus the bound ones)
        "01_Products", "04_ProductAreas", "06_CapabilityRequirements", "07_Adaptability",
        "10_Evidence", "11_Releases", "13_Risks", "14_ParallelPlan", "15_Dashboard",
    };

    public static SheetBinding? Binding(string logicalName) =>
        Sheets.FirstOrDefault(s => string.Equals(s.LogicalName, logicalName, StringComparison.Ordinal));

    public static string? PhysicalNameFor(SheetBinding b, WorkbookForm form) => form switch
    {
        WorkbookForm.LegacyL2 => b.LegacyName,
        WorkbookForm.V2Foundation => b.FoundationName,
        WorkbookForm.V2Products => b.ProductsName,
        WorkbookForm.V3 => b.V3Name,
        _ => null,
    };

    public static int? HeaderRowFor(SheetBinding b, WorkbookForm form) => form switch
    {
        WorkbookForm.LegacyL2 => b.LegacyHeaderRow,
        WorkbookForm.V2Foundation => b.FoundationHeaderRow,
        WorkbookForm.V2Products => b.ProductsHeaderRow,
        // V3's header row is uniform across all 26 sheets, so it is one measurement rather than
        // a per-sheet one. A sheet with no V3 counterpart has no header row to locate, and
        // returning null there keeps it ABSENT_IN_FORM rather than DECLARED_BUT_MISSING.
        WorkbookForm.V3 => b.V3Name is null ? null : V3HeaderRow,
        _ => null,
    };

    /// <summary>W8D-R TASK 4. WC-5's other half for the V3 model — the sheets present in the V3
    /// workbook that no logical binding consumes. They are named so that "nothing was silently
    /// ignored" stays a checkable claim. Several are not incidental: `09_ChangeScopes` and
    /// `11_Reservations` carry the scope and lease facts the collision engine consumes on the
    /// other three forms, and consolidating those into the logical projection is a ChangeScope
    /// change, not a binding change. Recorded here so the gap is visible rather than implied.</summary>
    public static readonly IReadOnlyList<string> KnownUnboundV3Sheets = new[]
    {
        "01_Configuration", "03_Products", "04_Capabilities", "05_Contracts",
        "09_ChangeScopes", "11_Reservations", "12_DevelopmentRuns",
        "13_GitLineage", "14_Builds", "15_Releases", "16_Deployments", "17_Evidence",
        "18_Governance", "19_ChangeRequests", "21_Risks", "22_Exceptions",
        "23_NexusEvolution", "24_V3MigrationMap", "25_Dashboard",
    };

    /// <summary>
    /// W8D-R TASK 4. WC-6 for the V3 model, stated rather than inferred.
    ///
    /// All three sheets this map marks <see cref="SheetBinding.GovernanceSubstrate"/> —
    /// VersionHistory, SessionProtocol and ActivityLog — are ABSENT_IN_FORM for V3, so the
    /// existing WC-6 rule already reports GOVERNANCE_SUBSTRATE_ABSENT and
    /// <see cref="WorkbookReadResult.MayGovern"/> is false.
    ///
    /// That verdict is NOT being softened here, and the reason matters. V3 does carry
    /// equivalent duties: the append-only version trail moved in-row into the envelope
    /// (RecordVersion | IsCurrent | EffectiveFrom | ChangeId | SupersedesVersion) that every
    /// governed sheet carries. But whether an in-row envelope satisfies ADR-003's append-only
    /// history requirement — and whether `11_Reservations` discharges the Activity Log's duty —
    /// is a governance question with a named human gate on it (W3-24 CONTROL_CUTOVER_GATE, per
    /// the workbook's own `00_Control`). Encoding "V3 may govern" in the reader would answer
    /// that question in code, which is exactly what W8D-R's directive forbids. So the reader
    /// reports the absence and the gate stays open.
    /// </summary>
    public const string V3GovernanceSubstrateDuties =
        "VersionHistory/SessionProtocol/ActivityLog are absent as SHEETS in V3; the version trail " +
        "is carried in-row by the envelope. Whether that satisfies WC-6/ADR-003 is reserved to " +
        "the W3-24 CONTROL_CUTOVER_GATE human decision and is not decided by this reader.";

    public static IEnumerable<ColumnBinding> ColumnsFor(string logicalSheet) =>
        Columns.Where(c => c.LogicalSheet == logicalSheet);
}

public static class WorkbookCompatibilityReader
{
    /// <summary>WC-8. Hash of the file as it lies on disk — this is also how W1-15 proves the
    /// authoritative workbooks were not modified.</summary>
    public static string Sha256Of(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    /// <summary>
    /// Reads a workbook. WC-7: the file is opened for READING only and is never written to; this
    /// type contains no write path.
    /// </summary>
    public static WorkbookReadResult Read(string path, WorkbookForm? formHint = null)
    {
        var diagnostics = new List<string>();

        if (!File.Exists(path))
            return Corrupt(path, "", diagnostics, $"The workbook does not exist at '{path}'.");

        string sha;
        try { sha = Sha256Of(path); }
        catch (Exception ex)
        {
            return Corrupt(path, "", diagnostics, $"The workbook could not be hashed: {ex.Message}");
        }

        List<string> sharedStrings;
        List<(string Name, string Part)> sheets;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            sharedStrings = ReadSharedStrings(zip);
            sheets = ReadSheetInventory(zip);
        }
        catch (Exception ex)
        {
            // An unreadable container is CORRUPT — never "empty". This is the distinction the
            // directive cares about: a file that cannot be parsed must not look like a file
            // that parsed and happened to contain nothing.
            return Corrupt(path, sha, diagnostics, $"The workbook is not a readable OOXML container: {ex.Message}");
        }

        var form = formHint ?? DetectForm(sha, sheets);
        var knownHash = WorkbookCompatibilityMap.FrozenHashFor(form);
        var matchesFrozen = knownHash is not null && string.Equals(knownHash, sha, StringComparison.OrdinalIgnoreCase);

        if (form == WorkbookForm.Unknown)
        {
            diagnostics.Add($"The sheet inventory does not match any of the three known forms. Sheets present: {string.Join(", ", sheets.Select(s => s.Name))}.");
            return new WorkbookReadResult(ReaderResult.UnsupportedSchema, WorkbookForm.Unknown, path, sha,
                false, Array.Empty<SheetRead>(), diagnostics, sharedStrings.Count, 0,
                sheets.Select(s => s.Name).ToArray(), Array.Empty<string>());
        }

        if (knownHash is not null && !matchesFrozen)
            diagnostics.Add($"This is a {form} workbook but its SHA-256 is {sha}; the frozen reference for that form is {knownHash}. " +
                            "The content is still read; the divergence is recorded because WC-8 identifies forms by hash.");

        var stats = new CellStats();
        var reads = new List<SheetRead>();
        var boundPhysicalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var zip = ZipFile.OpenRead(path))
        {
            foreach (var binding in WorkbookCompatibilityMap.Sheets)
            {
                var physical = WorkbookCompatibilityMap.PhysicalNameFor(binding, form);
                var headerRow = WorkbookCompatibilityMap.HeaderRowFor(binding, form);

                if (physical is null)
                {
                    reads.Add(new SheetRead(binding.LogicalName, SheetPresence.AbsentInForm, null, null, false,
                        Array.Empty<ColumnResolution>(), Array.Empty<WorkbookRecord>(), Array.Empty<string>(),
                        new[] { $"ABSENT_IN_FORM: the {form} form has no counterpart for '{binding.LogicalName}'." }, 0));
                    continue;
                }

                var entry = sheets.FirstOrDefault(s => string.Equals(s.Name, physical, StringComparison.OrdinalIgnoreCase));
                if (entry.Part is null)
                {
                    reads.Add(new SheetRead(binding.LogicalName, SheetPresence.DeclaredButMissing, physical, headerRow, false,
                        Array.Empty<ColumnResolution>(), Array.Empty<WorkbookRecord>(), Array.Empty<string>(),
                        new[] { $"DECLARED_BUT_MISSING: the {form} map binds '{binding.LogicalName}' to sheet '{physical}', which this workbook does not contain." }, 0));
                    continue;
                }

                boundPhysicalNames.Add(entry.Name);
                reads.Add(ReadSheet(zip, binding.LogicalName, entry.Name, entry.Part, headerRow, form, stats, sharedStrings));
            }
        }

        var unbound = sheets.Select(s => s.Name).Where(n => !boundPhysicalNames.Contains(n)).ToArray();
        var substrateAbsent = reads
            .Where(r => r.Presence == SheetPresence.AbsentInForm
                        && WorkbookCompatibilityMap.Binding(r.LogicalName)?.GovernanceSubstrate == true)
            .Select(r => r.LogicalName)
            .ToArray();

        if (substrateAbsent.Length > 0)
            diagnostics.Add($"GOVERNANCE_SUBSTRATE_ABSENT: the {form} form carries no {string.Join(", ", substrateAbsent)}. " +
                            "Per WC-6 this form cannot be used as a governing authority — it cannot satisfy ADR-003's append-only history requirement, " +
                            "carry the Session Protocol that constrains its own writers, or record who changed what.");

        var result = Derive(reads, diagnostics);
        return new WorkbookReadResult(result, form, path, sha, matchesFrozen, reads, diagnostics,
            sharedStrings.Count, stats.LiteralStrings, unbound, substrateAbsent);
    }

    /// <summary>
    /// WC-5's guard and the directive's explicit requirement: a recognised workbook must never
    /// come back "supported" with zero records. Each branch below is a distinct, named outcome.
    /// </summary>
    private static ReaderResult Derive(List<SheetRead> reads, List<string> diagnostics)
    {
        var bound = reads.Where(r => r.Presence == SheetPresence.Bound).ToArray();

        if (bound.Length == 0)
        {
            diagnostics.Add("No logical sheet could be bound to a physical sheet in this workbook.");
            return ReaderResult.UnsupportedSchema;
        }

        if (reads.Any(r => r.DecodeFailures.Count > 0))
        {
            diagnostics.Add($"{reads.Sum(r => r.DecodeFailures.Count)} cell(s) could not be decoded. Cells that fail to decode are never reported as empty — " +
                            "the read is PARTIAL so that no caller can mistake missing text for absent text.");
            return ReaderResult.PartiallySupported;
        }

        var missingRequired = bound.Where(r => !r.HasAllRequiredColumns).ToArray();
        if (missingRequired.Length > 0)
        {
            foreach (var r in missingRequired)
                diagnostics.Add($"UNSUPPORTED_SCHEMA: '{r.LogicalName}' was read from sheet '{r.PhysicalName}' but the required column(s) " +
                                $"{string.Join(", ", r.MissingRequiredColumns)} were not found in header row {r.HeaderRow}. " +
                                "A required column that cannot be found by name means this is not the sheet the map expected — " +
                                "reading it positionally would return a plausible value from an adjacent column and never fail.");
            return ReaderResult.UnsupportedSchema;
        }

        var unreadable = reads.Where(r => r.Presence == SheetPresence.Unreadable).ToArray();
        if (unreadable.Length > 0)
            return ReaderResult.PartiallySupported;

        if (reads.Any(r => r.Presence == SheetPresence.DeclaredButMissing))
            return ReaderResult.PartiallySupported;

        var total = reads.Sum(r => r.Records.Count);
        if (total == 0)
        {
            diagnostics.Add("The workbook is structurally valid and every bound sheet was located with all required columns present, " +
                            "but no sheet contained a governed record. This is EMPTY_VALID — it is NOT a successful read, and it is not " +
                            "the same as a workbook that governs nothing.");
            return ReaderResult.EmptyValid;
        }

        return ReaderResult.Supported;
    }

    private static WorkbookReadResult Corrupt(string path, string sha, List<string> diagnostics, string message)
    {
        diagnostics.Add(message);
        return new WorkbookReadResult(ReaderResult.Corrupt, WorkbookForm.Unknown, path, sha, false,
            Array.Empty<SheetRead>(), diagnostics, 0, 0, Array.Empty<string>(), Array.Empty<string>());
    }

    // ================================================================== W8D-R2 TASK 3 / TASK 10
    //
    // WRITE AUTHORISATION AND THE AUTHORITY RESOLVER.
    //
    // These are the ONLY write-side additions this change makes, and they are additive statics on
    // the type that already reads all four forms. There is deliberately NO writer here, NO new
    // lock, NO new lease, NO second identity scheme: the write path itself remains whatever
    // already holds the canonical lock (AtomicWriterLock under WorkbookWriterGate, whose named
    // object comes from SharedLockIdentity). What was missing was not a writer — it was a
    // written-down answer to "may this write happen at all", and a resolver that refuses to
    // substitute a different workbook when the canonical authority is unavailable.
    //
    // The write TARGET needs no new map. `PhysicalNameFor` and `HeaderRowFor` already resolve the
    // V3 physical sheet and its uniform header row, and `ColumnsFor` already carries the V3
    // column names with their occurrence index. A second, write-only sheet/column map would be a
    // duplicate of knowledge that is already here, which is exactly what the directive forbids.
    //
    // The failure this exists to prevent is the same one the read side prevents, one layer up: a
    // writer that cannot recognise the workbook, writes anyway, and reports success.

    /// <summary>The V3 schema identity. Read from `00_Control` and compared — never assumed.</summary>
    public const string V3SchemaId = "NEXUS-DEVELOPMENTCONTROL";
    public const string V3SchemaVersion = "V3.0";

    /// <summary>
    /// The authority marker's item name and its closed vocabulary. `W3_SCHEMA.md:367` defines the
    /// cutover act literally as "Set ControlState = AUTHORITATIVE", so this string pair IS the
    /// migration state machine, not a label.
    /// </summary>
    public const string AuthorityMarkerItem = "ControlState";
    public const string AuthorityMarkerCandidate = "CANDIDATE";
    public const string AuthorityMarkerAuthoritative = "AUTHORITATIVE";

    /// <summary>
    /// The Owner's W8D-R2 designation for the 14-sheet workbook. It is NOT part of the V3 schema's
    /// own vocabulary — a scan of all 26 sheets' shared strings finds no occurrence of it — so it
    /// is introduced here as a named constant rather than pretended to be read from the workbook.
    /// </summary>
    public const string LegacyReadOnlyDesignation = "LEGACY_READ_ONLY";

    /// <summary>Why a write was refused. Every refusal is typed; none is a bare `false`.</summary>
    public enum WriteVerdict
    {
        Permitted = 0,
        RefusedSchemaUnrecognised = 1,
        RefusedSchemaMismatch = 2,
        RefusedSchemaVersionMismatch = 3,
        RefusedLegacyReadOnly = 4,
        RefusedAuthorityUnavailable = 5,
        RefusedAuthorityMarkerUnreadable = 6,
    }

    public sealed record WriteAuthorisation(
        WriteVerdict Verdict,
        bool Allowed,
        WorkbookForm Form,
        string TargetPath,
        string SchemaId,
        string SchemaVersion,
        string ControlState,
        string Reason);

    /// <summary>
    /// Projects `00_Control`'s key/value block. Uses the SAME bindings the reader already resolved
    /// for the `Control` logical sheet, so a mistyped header fails here exactly as it fails there.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ControlItems(WorkbookReadResult read)
    {
        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        var control = read.Sheet("Control");
        if (control is null) return items;
        foreach (var record in control.Records)
        {
            var key = record.Get("ControlItem");
            if (string.IsNullOrWhiteSpace(key)) continue;
            items[key.Trim()] = record.Get("Value") ?? "";
        }
        return items;
    }

    /// <summary>
    /// The authority marker. Returns null when it is not readable, which is DISTINCT from an empty
    /// value: "this workbook does not carry an authority marker" and "this workbook says it is not
    /// authoritative" are different facts and only one of them is a candidate for a write.
    /// </summary>
    public static string? AuthorityMarker(WorkbookReadResult read) =>
        ControlItems(read).TryGetValue(AuthorityMarkerItem, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v.Trim()
            : null;

    /// <summary>
    /// The write gate. Fails CLOSED on an unrecognised form — an adapter that answers "ok" for a
    /// workbook it could not read is the defect, not the safety net.
    ///
    /// Note what is NOT checked: `MayGovern` / `GovernanceSubstrateAbsent`. V3 legitimately has no
    /// separate Version History / Session Protocol / Activity Log SHEET because its envelope
    /// carries them per row. Whether that satisfies WC-6/ADR-003 is reserved — by
    /// `V3GovernanceSubstrateDuties` and by `W3_SCHEMA.md` — to the human cutover decision. A
    /// reader that refused every V3 write on a question it was told to reserve would make that
    /// decision for the Owner, which is a larger error than the one it would be avoiding. It is
    /// therefore REPORTED in the reason string and not used to refuse.
    /// </summary>
    public static WriteAuthorisation AuthorizeWrite(WorkbookReadResult read, WorkbookForm intended)
    {
        var items = ControlItems(read);
        items.TryGetValue("SchemaId", out var schemaId);
        items.TryGetValue("SchemaVersion", out var schemaVersion);
        var state = AuthorityMarker(read) ?? "";
        var substrate = read.GovernanceSubstrateAbsent.Count;

        WriteAuthorisation Refuse(WriteVerdict verdict, string reason) =>
            new(verdict, false, read.Form, read.SourcePath, schemaId ?? "", schemaVersion ?? "", state, reason);

        if (read.Result is ReaderResult.UnsupportedSchema or ReaderResult.Corrupt)
            return Refuse(WriteVerdict.RefusedSchemaUnrecognised,
                $"FAIL_CLOSED — the workbook at '{read.SourcePath}' was not recognised (result={read.Result}). " +
                "No write may proceed against a workbook that could not be read: 'I could not read this' and " +
                "'this workbook is empty' are different answers and only one of them is safe.");

        if (read.Form != intended)
            return Refuse(WriteVerdict.RefusedSchemaMismatch,
                $"SCHEMA_MISMATCH — the workbook is {read.Form} but the caller declared {intended}. " +
                "Writing would silently convert one schema into another.");

        if (read.Form == WorkbookForm.V3 &&
            (!string.Equals(schemaId, V3SchemaId, StringComparison.Ordinal) ||
             !string.Equals(schemaVersion, V3SchemaVersion, StringComparison.Ordinal)))
            return Refuse(WriteVerdict.RefusedSchemaVersionMismatch,
                $"SCHEMA_VERSION_MISMATCH — SchemaId='{schemaId}' SchemaVersion='{schemaVersion}', this build writes " +
                $"'{V3SchemaId}' / '{V3SchemaVersion}'.");

        if (read.Form == WorkbookForm.V3 && state.Length == 0)
            return Refuse(WriteVerdict.RefusedAuthorityMarkerUnreadable,
                $"AUTHORITY_MARKER_UNREADABLE — V3 was recognised by its sheets and by SchemaId=" +
                $"'{schemaId ?? ""}', but `00_Control` carries no readable `{AuthorityMarkerItem}` item. " +
                "That item is what the cutover flips and what the fallback guard reads, so a V3 whose state " +
                "cannot be established must not be written.");

        return new WriteAuthorisation(WriteVerdict.Permitted, true, read.Form, read.SourcePath,
            schemaId ?? "", schemaVersion ?? "", state,
            $"permitted — {read.Form} recognised and declared" +
            (read.Form == WorkbookForm.V3 ? $"; {AuthorityMarkerItem}='{state}'" : "") +
            (substrate > 0
                ? $"; NOTE: {substrate} governance-substrate sheet(s) are absent from this form. Whether its " +
                  "in-row envelope satisfies WC-6/ADR-003 is reserved to the W3-24 human decision and is NOT " +
                  "used to refuse this write."
                : ""));
    }

    public static WriteAuthorisation AuthorizeWriteForPath(string path, WorkbookForm intended) =>
        AuthorizeWrite(Read(path), intended);

    /// <summary>
    /// The path-class refusal, which is a DIFFERENT question from the schema gate: "may this
    /// particular FILE be written", not "may this SCHEMA be written". Kept separate so a caller can
    /// report which question failed.
    ///
    /// Two path classes are refused, and only two: the canonical legacy workbook (the Owner's
    /// LEGACY_READ_ONLY designation) and anything under `C:\Personal` (the canonical V3 root rule).
    /// A disposable path — a temp fixture, a governed worktree copy — is NOT refused here, because
    /// refusing it would break every existing fixture proof and would conflate "not the authority"
    /// with "not writable".
    /// </summary>
    public static (bool Allowed, string Reason) AuthorizeWritePath(string path)
    {
        var normalized = NormalizePath(path);

        if (normalized.EndsWith(NormalizePath(WorkbookCompatibilityMap.LegacyAuthorityPath), StringComparison.Ordinal))
            return (false,
                $"{LegacyReadOnlyDesignation} — '{path}' is the 14-sheet workbook, designated " +
                $"{LegacyReadOnlyDesignation} by the Owner's W8D-R2 decision. It is PRESERVED, not deleted, and " +
                "no active writer may target it: a write here would diverge silently from the canonical V3 authority.");

        if (normalized.StartsWith(NormalizePath(WorkbookCompatibilityMap.PersonalRootPrefix), StringComparison.Ordinal))
            return (false,
                $"AUTHORITY_UNAVAILABLE — '{path}' is under {WorkbookCompatibilityMap.PersonalRootPrefix}, which is not a " +
                "governed Nexus root. The canonical V3 authority has no location there and no fallback copy may be " +
                "created there.");

        return (true, $"permitted — '{path}' is not a refused path class");
    }

    /// <summary>
    /// TASK 10's guard, and the one place a caller must go through to learn where the authority is.
    ///
    /// It NEVER substitutes another workbook. When the canonical V3 authority is unavailable for any
    /// reason it returns Available=false with a named reason, and the caller fails explicitly. The
    /// three fallbacks the directive names — the product-local workbook, the legacy 14-sheet
    /// workbook, and a `C:\Personal` copy — are each named in the refusal so the failure says which
    /// one was NOT silently chosen.
    /// </summary>
    public static AuthorityResolution ResolveAuthority(string configuredPath)
    {
        const string Refused = "no fallback is chosen. The product-local workbook, the legacy 14-sheet workbook " +
                               "and any C:\\Personal copy are each refused as substitutes.";

        if (string.IsNullOrWhiteSpace(configuredPath))
            return new AuthorityResolution(false, "", WorkbookCompatibilityMap.CanonicalAuthorityPath,
                "AUTHORITY_UNAVAILABLE — no DevelopmentControl workbook path was configured. " + Refused);

        var normalized = NormalizePath(configuredPath);
        if (!normalized.EndsWith(NormalizePath(WorkbookCompatibilityMap.CanonicalAuthorityPath), StringComparison.Ordinal))
            return new AuthorityResolution(false, configuredPath, WorkbookCompatibilityMap.CanonicalAuthorityPath,
                $"AUTHORITY_UNAVAILABLE — the configured workbook '{configuredPath}' is not the canonical V3 authority " +
                $"'{WorkbookCompatibilityMap.CanonicalAuthorityPath}'. " + Refused);

        if (!File.Exists(configuredPath))
            return new AuthorityResolution(false, configuredPath, WorkbookCompatibilityMap.CanonicalAuthorityPath,
                $"AUTHORITY_UNAVAILABLE — the canonical V3 authority '{configuredPath}' does not exist. " + Refused);

        WorkbookReadResult read;
        try { read = Read(configuredPath); }
        catch (Exception ex)
        {
            return new AuthorityResolution(false, configuredPath, WorkbookCompatibilityMap.CanonicalAuthorityPath,
                $"AUTHORITY_UNAVAILABLE — the canonical V3 authority '{configuredPath}' could not be read: " +
                $"{ex.GetType().Name}: {ex.Message}. " + Refused);
        }

        if (read.Form != WorkbookForm.V3)
            return new AuthorityResolution(false, configuredPath, WorkbookCompatibilityMap.CanonicalAuthorityPath,
                $"AUTHORITY_UNAVAILABLE — '{configuredPath}' reads as {read.Form}, not V3. " + Refused);

        var state = AuthorityMarker(read);
        if (state is null)
            return new AuthorityResolution(false, configuredPath, WorkbookCompatibilityMap.CanonicalAuthorityPath,
                $"AUTHORITY_UNAVAILABLE — the canonical V3 authority carries no readable `{AuthorityMarkerItem}` item. " +
                Refused);

        return new AuthorityResolution(true, configuredPath, WorkbookCompatibilityMap.CanonicalAuthorityPath,
            $"AVAILABLE — {configuredPath} reads as V3 ({read.RecordCount} records, {read.Sheets.Count} sheets), " +
            $"{AuthorityMarkerItem}='{state}'");
    }

    /// <summary>Case- and separator-insensitive, so a forward-slash or mixed-case path is not a bypass.</summary>
    internal static string NormalizePath(string path) =>
        path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();

    // ------------------------------------------------------------------ OOXML plumbing

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var result = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return result; // V2 forms legitimately have none.

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var si in doc.Root!.Elements(m + "si"))
            result.Add(string.Concat(si.Descendants(m + "t").Select(t => t.Value)));
        return result;
    }

    private static List<(string Name, string Part)> ReadSheetInventory(ZipArchive zip)
    {
        var list = new List<(string, string)>();
        var wbEntry = zip.GetEntry("xl/workbook.xml");
        if (wbEntry is null) return list;

        XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        XDocument wb;
        using (var s = wbEntry.Open()) wb = XDocument.Load(s);

        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        var relEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (relEntry is not null)
        {
            using var s = relEntry.Open();
            var rels = XDocument.Load(s);
            foreach (var rel in rels.Root!.Elements())
            {
                var id = rel.Attribute("Id")?.Value;
                var target = rel.Attribute("Target")?.Value;
                if (id is not null && target is not null) targets[id] = target;
            }
        }

        foreach (var sheet in wb.Root!.Element(m + "sheets")?.Elements(m + "sheet") ?? Enumerable.Empty<XElement>())
        {
            var name = sheet.Attribute("name")?.Value;
            var rid = sheet.Attribute(r + "id")?.Value;
            if (name is null) continue;

            string? part = null;
            if (rid is not null && targets.TryGetValue(rid, out var target)) part = NormalizePart(target);
            // Fall back to positional resolution only if the relationship is missing entirely.
            part ??= $"xl/worksheets/sheet{list.Count + 1}.xml";
            list.Add((name, part));
        }

        return list;

        // Targets appear BOTH ways in the three frozen forms: legacy uses the relative form
        // ("worksheets/sheet2.xml") and both V2 forms use the absolute form
        // ("/xl/worksheets/sheet1.xml"). A reader that handles only one of the two finds no
        // sheets at all in half the estate.
        static string NormalizePart(string target)
        {
            var t = target.Replace('\\', '/');
            if (t.StartsWith('/')) t = t[1..];
            return t.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? t : "xl/" + t;
        }
    }

    /// <summary>A mutable cell counter. The decode path is a plain method rather than an iterator
    /// so that a failed decode can be RECORDED and the cell withheld — an iterator cannot carry a
    /// ref parameter, and silently dropping the record would be the very thing WC-3 forbids.</summary>
    internal sealed class CellStats
    {
        /// <summary>
        /// Cells whose string is carried IN the cell rather than in the shared-string table —
        /// both <c>t="inlineStr"</c> and <c>t="str"</c>.
        /// <para>The two are counted together because the trap this counter exists to expose
        /// does not distinguish them: a reader that understands only <c>t="s"</c> reads BOTH as
        /// empty. Counted separately, <c>inlineStr</c> alone would report 0 for both V2
        /// workbooks — which are built entirely from <c>t="str"</c> — and the counter would
        /// report "no inline strings" about a workbook in which every single string is inline.</para>
        /// </summary>
        public int LiteralStrings;
    }

    private static SheetRead ReadSheet(
        ZipArchive zip, string logical, string physical, string part, int? headerRow,
        WorkbookForm form, CellStats stats, List<string> sharedStrings)
    {
        XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var decodeFailures = new List<string>();
        var diagnostics = new List<string>();
        XDocument doc;
        try
        {
            var entry = zip.GetEntry(part);
            if (entry is null)
                return new SheetRead(logical, SheetPresence.Unreadable, physical, headerRow, false,
                    Array.Empty<ColumnResolution>(), Array.Empty<WorkbookRecord>(), Array.Empty<string>(),
                    new[] { $"The sheet part '{part}' is named in the workbook but absent from the container." }, 0);
            using var s = entry.Open();
            doc = XDocument.Load(s);
        }
        catch (Exception ex)
        {
            return new SheetRead(logical, SheetPresence.Unreadable, physical, headerRow, false,
                Array.Empty<ColumnResolution>(), Array.Empty<WorkbookRecord>(), Array.Empty<string>(),
                new[] { $"The sheet part '{part}' could not be parsed: {ex.Message}" }, 0);
        }

        var rows = doc.Root!.Element(m + "sheetData")?.Elements(m + "row").ToArray() ?? Array.Empty<XElement>();
        var byNumber = new Dictionary<int, XElement>();
        var lastRow = 0;
        foreach (var row in rows)
        {
            if (!int.TryParse(row.Attribute("r")?.Value, out var n)) continue;
            byNumber[n] = row;
            if (n > lastRow) lastRow = n;
        }

        // --- header row location (WC-1)
        var headerRowLocated = false;
        var headerValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headerRow is int hr && byNumber.TryGetValue(hr, out var headerElement))
        {
            headerRowLocated = true;
            foreach (var (col, value) in Cells(headerElement, m, sharedStrings, decodeFailures, physical, hr, stats))
                headerValues[col] = value;
        }

        // --- column resolution BY NAME (WC-2). Index access does not appear anywhere below.
        var bindings = WorkbookCompatibilityMap.ColumnsFor(logical).ToArray();
        var resolutions = new List<ColumnResolution>();
        var letterFor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cb in bindings)
        {
            var wanted = form switch
            {
                WorkbookForm.LegacyL2 => cb.Legacy,
                WorkbookForm.V2Foundation => cb.Foundation,
                WorkbookForm.V2Products => cb.Products,
                WorkbookForm.V3 => cb.V3,
                _ => null,
            };

            // W8D-R TASK 4. Requiredness is per-form for exactly one column: see
            // ColumnBinding.V3Required. Null means inherit, so the three frozen forms are
            // bit-for-bit unaffected.
            var required = form == WorkbookForm.V3 ? cb.V3Required ?? cb.Required : cb.Required;

            if (wanted is null)
            {
                // This form carries no such column at all. Found=false is the truthful answer and
                // it is what ProjectScope needs: a missing column must not look like a present,
                // empty one — that is the difference between "no scope declared" and "scope not
                // readable", and the two must not collapse. Required is false for these, so this
                // never trips the UnsupportedSchema rule.
                resolutions.Add(new ColumnResolution(cb.LogicalName, null, false, required));
                continue;
            }

            // WC-2, refined by W8D-R TASK 4. Resolution is still strictly BY NAME, and never by
            // position — but where a form declares the same header name more than once the
            // OCCURRENCE is now declared explicitly rather than left to dictionary enumeration
            // order. Ordering by the physical column index makes "the first occurrence" a
            // property of the sheet rather than of an implementation detail.
            var letter = headerValues
                .Where(kv => string.Equals(kv.Value.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => ColumnIndex(kv.Key))
                .Skip(cb.V3Occurrence - 1)
                .Select(kv => kv.Key)
                .FirstOrDefault();

            if (letter is null)
                resolutions.Add(new ColumnResolution(cb.LogicalName, wanted, false, required));
            else
            {
                resolutions.Add(new ColumnResolution(cb.LogicalName, wanted, true, required));
                letterFor[cb.LogicalName] = letter;
            }
        }

        // --- data rows.
        //
        // A sheet is read GENERICALLY (cells keyed by their physical column letter) when the
        // header row is absent by convention — legacy `Control Center` — AND when no logical
        // column of this form could be resolved at all. The second case matters: without it, a
        // logical sheet whose columns W1 has no measured header for would silently contribute
        // ZERO records while the workbook still reported success. That is the exact
        // "recognised, read the wrong thing, returned nothing, called it success" failure the
        // directive forbids, so the fallback is deliberate and it is reported.
        var generic = headerRow is null || letterFor.Count == 0;
        if (generic && bindings.Length > 0)
            diagnostics.Add($"GENERIC_READ: no logical column of the {form} map resolved against sheet '{physical}', " +
                            "so its rows are reported with their physical column letters. Nothing is dropped and nothing is claimed.");

        var dataStart = headerRow is int h ? h + 1 : 1;
        var records = new List<WorkbookRecord>();

        foreach (var n in byNumber.Keys.Where(k => k >= dataStart).OrderBy(k => k))
        {
            var cells = Cells(byNumber[n], m, sharedStrings, decodeFailures, physical, n, stats);
            if (cells.Count == 0) continue;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (generic)
            {
                foreach (var (col, value) in cells)
                    if (!string.IsNullOrEmpty(value)) values[col] = value;
            }
            else
            {
                foreach (var logicalColumn in letterFor.Keys)
                {
                    var letter = letterFor[logicalColumn];
                    var match = cells.FirstOrDefault(c => string.Equals(c.Col, letter, StringComparison.OrdinalIgnoreCase));
                    if (match.Col is not null && !string.IsNullOrEmpty(match.Value))
                        values[logicalColumn] = match.Value;
                }
            }

            if (values.Count > 0)
                records.Add(new WorkbookRecord(logical, n, values));
        }

        if (bindings.Length > 0 && headerRow is not null && !headerRowLocated)
            diagnostics.Add($"Header row {headerRow} is absent from sheet '{physical}'; the sheet may be empty.");

        return new SheetRead(logical, SheetPresence.Bound, physical, headerRow, headerRowLocated,
            resolutions, records, decodeFailures, diagnostics, lastRow);
    }

    /// <summary>
    /// WC-3, in one function. Shared strings, inline strings and every other cell type decode
    /// here, and anything that cannot be decoded is appended to <paramref name="decodeFailures"/>
    /// AND raised — it is never allowed to become an empty string, because an empty string is
    /// indistinguishable from a genuinely blank cell.
    /// </summary>
    private static List<(string Col, string Value)> Cells(
        XElement row, XNamespace m, List<string> sharedStrings,
        List<string> decodeFailures, string sheet, int rowNumber, CellStats stats)
    {
        XNamespace ns = m;
        var result = new List<(string, string)>();
        var position = 0;
        foreach (var c in row.Elements(ns + "c"))
        {
            position++;
            var reference = c.Attribute("r")?.Value;
            var col = reference is not null ? ColumnLetters(reference) : ColumnLettersForIndex(position);
            var type = c.Attribute("t")?.Value;
            string value;

            try
            {
                value = Decode(c, type, ns, sharedStrings, stats);
            }
            catch (WorkbookDecodeException ex)
            {
                decodeFailures.Add($"{sheet}!{col}{rowNumber}: {ex.Message}");
                continue; // the failure is recorded; the cell contributes NO value
            }

            result.Add((col, value));
        }
        return result;
    }

    internal static string Decode(XElement c, string? type, XNamespace ns, List<string> sharedStrings, CellStats stats)
    {
        switch (type)
        {
            case "s":
            {
                var raw = c.Element(ns + "v")?.Value;
                if (!int.TryParse(raw, out var index))
                    throw new WorkbookDecodeException($"a shared-string cell carries a non-numeric index '{raw ?? "(none)"}'");
                if (index < 0 || index >= sharedStrings.Count)
                    throw new WorkbookDecodeException($"shared-string index {index} is outside the {sharedStrings.Count}-entry table");
                return sharedStrings[index];
            }

            case "inlineStr":
            {
                stats.LiteralStrings++;
                var isElement = c.Element(ns + "is");
                if (isElement is null)
                    throw new WorkbookDecodeException("an inline-string cell has no <is> element");
                // Rich text splits the value across several <r><t> runs; concatenating them is
                // the only correct reading. Taking the first would silently truncate.
                return string.Concat(isElement.Descendants(ns + "t").Select(t => t.Value));
            }

            case "str":
                // The V2 forms' ONLY string encoding: the text sits in <v> with no shared-string
                // table anywhere in the package. Counted with inlineStr because it is the same
                // trap — see CellStats.LiteralStrings.
                stats.LiteralStrings++;
                return c.Element(ns + "v")?.Value ?? "";

            case "b":
                return c.Element(ns + "v")?.Value == "1" ? "TRUE" : "FALSE";

            case "e":
                return c.Element(ns + "v")?.Value ?? "";

            case null:
            case "n":
                return c.Element(ns + "v")?.Value ?? "";

            default:
                throw new WorkbookDecodeException($"cell type '{type}' is not one this reader recognises");
        }
    }

    private static string ColumnLetters(string cellReference)
    {
        var letters = new string(cellReference.TakeWhile(char.IsLetter).ToArray());
        return letters.Length == 0 ? "?" : letters.ToUpperInvariant();
    }

    /// <summary>W8D-R TASK 4. The inverse of <see cref="ColumnLettersForIndex"/>, used to order
    /// duplicate header names by physical position. Returns int.MaxValue for a letter run that
    /// is not a column reference, so such a key sorts last rather than first.</summary>
    private static int ColumnIndex(string letters)
    {
        if (string.IsNullOrEmpty(letters)) return int.MaxValue;
        var n = 0;
        foreach (var ch in letters)
        {
            if (ch < 'A' || ch > 'Z') return int.MaxValue;
            n = n * 26 + (ch - 'A' + 1);
        }
        return n;
    }

    private static string ColumnLettersForIndex(int oneBased)
    {
        var sb = new System.Text.StringBuilder();
        var n = oneBased;
        while (n > 0)
        {
            var rem = (n - 1) % 26;
            sb.Insert(0, (char)('A' + rem));
            n = (n - 1) / 26;
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ form detection

    /// <summary>WC-8: hash first, inventory second. The hash is authoritative when it matches a
    /// frozen reference; the inventory covers workbooks that have legitimately moved on.</summary>
    private static WorkbookForm DetectForm(string sha, List<(string Name, string Part)> sheets)
    {
        if (string.Equals(sha, WorkbookCompatibilityMap.LegacyFrozenSha256, StringComparison.OrdinalIgnoreCase))
            return WorkbookForm.LegacyL2;
        if (string.Equals(sha, WorkbookCompatibilityMap.FoundationFrozenSha256, StringComparison.OrdinalIgnoreCase))
            return WorkbookForm.V2Foundation;
        if (string.Equals(sha, WorkbookCompatibilityMap.ProductsFrozenSha256, StringComparison.OrdinalIgnoreCase))
            return WorkbookForm.V2Products;

        var names = sheets.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // V3 first, and by the sheets only V3 has. The numeric sheet prefix is NOT sufficient to
        // identify V3 — both V2 forms prefix their sheets numerically too — so the test is the
        // three V3 sheets that no other model carries. Requiring all three rather than one keeps
        // a partially-written workbook from being classified as V3 on a single coincidence.
        if (names.Contains("07_WorkItems") && names.Contains("08_Dependencies") && names.Contains("09_ChangeScopes"))
            return WorkbookForm.V3;

        if (names.Contains("Control Center") && names.Contains("Master Roadmap")) return WorkbookForm.LegacyL2;
        if (names.Contains("04_Tasks") && names.Contains("03_Milestones")) return WorkbookForm.V2Foundation;
        if (names.Contains("03_Tasks") && names.Contains("01_Products")) return WorkbookForm.V2Products;
        return WorkbookForm.Unknown;
    }

    // ------------------------------------------------------------------ W1-05 projection

    /// <summary>
    /// Projects the scope-bearing columns a workbook actually carries into the collision engine's
    /// input type. This exists so the compatibility reader is USED rather than merely present:
    /// M1C's interop defect I-4 is precisely "the derivation exists but nothing consumes it", and
    /// a reader whose output no engine reads would reproduce that defect in a new place.
    ///
    /// Where a form carries no scope column (both V2 forms today, for work items) this returns an
    /// empty list AND a diagnostic — an empty scope is not an empty risk (W1-04's rule), so the
    /// caller can tell "no scope declared" from "scope not readable".
    /// </summary>
    public static (IReadOnlyList<ScopeItem> Scope, IReadOnlyList<string> Diagnostics) ProjectScope(
        WorkbookReadResult read, string repositoryId)
    {
        var scope = new List<ScopeItem>();
        var diags = new List<string>();

        var workGraph = read.Sheet("WorkGraph");
        if (workGraph is null)
        {
            diags.Add("The workbook carries no WorkGraph sheet, so no scope can be projected.");
            return (scope, diags);
        }

        if (!workGraph.Columns.Any(c => c.LogicalName == "FilesGlobs" && c.Found))
        {
            diags.Add($"The {read.Form} form carries no 'Files / Globs' column on its work-item sheet, so no file scope can be projected from it. " +
                      "This is ABSENT_IN_FORM, not an empty scope.");
            return (scope, diags);
        }

        var declaredRows = 0;
        foreach (var record in workGraph.Records)
        {
            var raw = record.Get("FilesGlobs");
            if (string.IsNullOrWhiteSpace(raw)) continue;
            declaredRows++;
            foreach (var token in raw.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kind = token.Contains('*') || token.Contains('?') ? ScopeKind.Glob
                         : token.EndsWith('\\') || token.EndsWith('/') ? ScopeKind.DirectorySubtree
                         : ScopeKind.ExactFile;
                scope.Add(new ScopeItem(kind, repositoryId, token, AccessMode.Write, record.Get("WorkId") ?? ""));
            }
        }

        // Three states, never two. A projection that returns an empty list in silence would be
        // read by its caller as "nothing to check" — which is the W1-04 rule (an empty scope is
        // not an empty risk) applied to the reader.
        if (declaredRows == 0)
            diags.Add($"The 'Files / Globs' column was located on the {read.Form} work-item sheet but no record declared a scope. " +
                      "An empty projection is NOT the same as a validated empty scope.");

        return (scope, diags);
    }

    // ------------------------------------------------- W1 Closure: reservation-ledger projection

    /// <summary>
    /// Projects the reservation ledger (<c>Changes</c>) into what the LIVE collision gate needs:
    /// each reservation's own declared scope, whether it is still open, and — critically —
    /// whether its scope could be read at all.
    /// <para><b>Why <see cref="ScopeReadable"/> is a separate field and not inferred from an
    /// empty scope list.</b> The two are different facts and the gate must act differently on
    /// them. A reservation that declared no files genuinely conflicts with nothing. A
    /// reservation whose scope <em>columns are absent from the workbook form</em> is
    /// unmeasured, and a gate that treated unmeasured as empty would clear every candidate
    /// against it. That is the W1-05 defect in a new place, so the two are separated here
    /// rather than at the call site, where it would be easy to forget.</para>
    /// <para>Openness uses the ledger's own documented leading-keyword rule (a Status beginning
    /// Completed/Cancelled is terminal) — the same rule <c>ActiveChangesSnapshot</c> already
    /// applies — so the reader and the snapshot cannot disagree about which rows are live.</para>
    /// </summary>
    public static (IReadOnlyList<HeldReservation> Reservations, IReadOnlyList<string> Diagnostics) ProjectReservations(
        WorkbookReadResult read, string defaultRepositoryId)
    {
        var held = new List<HeldReservation>();
        var diags = new List<string>();

        var sheet = read.Sheet("Changes");
        if (sheet is null)
        {
            diags.Add($"The {read.Form} workbook carries no reservation ledger sheet, so no held reservation could be projected. " +
                      "This is NOT 'no reservations held'.");
            return (held, diags);
        }

        bool Has(string logical) => sheet.Columns.Any(c => c.LogicalName == logical && c.Found);
        bool anyScopeColumn = Has("FilesGlobs") || Has("Projects") || Has("ContractsApis") || Has("Repositories");
        if (!anyScopeColumn)
        {
            diags.Add($"The {read.Form} reservation ledger carries none of Repositories / Projects / Files / Globs / Contracts / APIs, " +
                      "so no held reservation's scope can be projected. ABSENT_IN_FORM — the gate must treat every held reservation " +
                      "as unmeasured, never as scope-free.");
        }

        foreach (var record in sheet.Records)
        {
            var id = record.Get("ChangeId") ?? "";
            if (string.IsNullOrWhiteSpace(id)) continue;

            var status = record.Get("Status") ?? "";
            bool open = !(status.TrimStart().StartsWith("Completed", StringComparison.OrdinalIgnoreCase)
                       || status.TrimStart().StartsWith("Cancelled", StringComparison.OrdinalIgnoreCase));

            var repos = Split(record.Get("Repositories"));
            if (repos.Count == 0 && !string.IsNullOrWhiteSpace(defaultRepositoryId)) repos.Add(defaultRepositoryId);

            var itemScope = new List<ScopeItem>();
            foreach (var repo in repos)
            {
                foreach (var token in Split(record.Get("FilesGlobs")))
                    itemScope.Add(new ScopeItem(KindOf(token), repo, token, AccessMode.Write));

                foreach (var proj in Split(record.Get("Projects")))
                    itemScope.Add(new ScopeItem(ScopeKind.ProjectResource, repo, proj, AccessMode.Write));
            }
            foreach (var contract in Split(record.Get("ContractsApis")))
                itemScope.Add(new ScopeItem(ScopeKind.PublicContract, defaultRepositoryId, contract, AccessMode.Write));

            held.Add(new HeldReservation(
                ChangeId: id,
                WorkId: record.Get("ImplementationWorkId") ?? "",
                Status: status,
                Open: open,
                Scope: itemScope,
                ScopeReadable: anyScopeColumn));
        }

        return (held, diags);
    }

    private static ScopeKind KindOf(string token) =>
        token.Contains('*') || token.Contains('?') ? ScopeKind.Glob
        : token.EndsWith('\\') || token.EndsWith('/') ? ScopeKind.DirectorySubtree
        : ScopeKind.ExactFile;

    /// <summary>Reservation scope fields are stored pipe-joined by DB-M04; work-item scope is
    /// semicolon-joined. Both are accepted, plus newlines, because a gate that silently read
    /// one separator as a single token would under-report every multi-entry scope.</summary>
    private static List<string> Split(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? new List<string>()
            : raw.Split(new[] { '|', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

/// <summary>One row of the reservation ledger, as the live collision gate sees it.</summary>
public sealed record HeldReservation(
    string ChangeId,
    string WorkId,
    string Status,
    bool Open,
    IReadOnlyList<ScopeItem> Scope,
    /// <summary>False when the workbook form carries no scope columns at all. An unreadable
    /// scope is unmeasured, and an unmeasured collision is not a clear one.</summary>
    bool ScopeReadable);
