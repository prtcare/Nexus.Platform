namespace Nexus.ProductCore.Contracts.DevelopmentControl;

/// <summary>
/// W8D-R4 TASK 3. The stable DevelopmentControl surface shared by Forge and Nexus.Developer.
///
/// <para><b>Why this file exists.</b> Owner decision R4-01 requires ONE canonical
/// DevelopmentControl reader/writer/locking implementation, consumed by both hosts. The
/// implementation lives in <c>Nexus.ProductCore.Core</c>. The interfaces it is consumed
/// through live here, in the contracts assembly, because Nexus.Developer's boundary guard
/// permits a product to reference Product Core <i>contracts</i> and nothing else - see
/// <c>Directory.Build.props</c> in the Developer repository and
/// <c>W8DR4_COMPONENT_OWNERSHIP.md</c>. Developer therefore depends on this file and never on
/// the implementation assembly.</para>
///
/// <para><b>OOXML stays behind this line.</b> TASK 3 requires that "callers must not depend on
/// OOXML internals" and that "workbook schema mechanics remain implementation detail". Nothing
/// in this namespace names an XML, ZIP, stream or sheet-format type. A reader returns decoded
/// records; how the container was opened is not part of the contract and may change without
/// breaking a caller.</para>
///
/// <para><b>What is deliberately NOT here.</b> No file paths to a specific workbook, no
/// directory layout, no host configuration object. The Forge implementation's writer gate is
/// currently keyed on <c>DevBridgeConfig</c>, a Forge-only type; a shared contract cannot name
/// it, so callers pass explicit paths and each host derives its own layout. This is the
/// dependency inversion that lets the same implementation serve Forge and Developer without
/// either host's configuration type crossing the boundary.</para>
/// </summary>

#region Vocabulary shared by every contract

/// <summary>
/// Which physical form a workbook is in. The reader must cope with more than one because the
/// 14-sheet legacy revisions, the V2 split workbooks and the 26-sheet V3 candidate all exist
/// on disk simultaneously during the migration.
/// </summary>
public enum DevelopmentControlForm
{
    /// <summary>Not yet determined, or not determinable.</summary>
    Unknown = 0,

    /// <summary>The 14-sheet legacy workbook.</summary>
    Legacy = 1,

    /// <summary>The V2 Foundation workbook.</summary>
    Foundation = 2,

    /// <summary>The V2 Products workbook.</summary>
    Products = 3,

    /// <summary>The 26-sheet V3 candidate.</summary>
    V3 = 4,
}

/// <summary>
/// The authority a workbook claims for itself, read from its own control sheet.
///
/// <para>A workbook may be structurally valid and still not be authoritative. The distinction
/// is the whole reason the cutover is gated: a writer that cannot tell the two apart will
/// happily write to a candidate.</para>
/// </summary>
public enum DevelopmentControlAuthority
{
    Unknown = 0,

    /// <summary>Declared authoritative. Writable.</summary>
    Authoritative = 1,

    /// <summary>Declared a candidate. Readable, and <b>not</b> writable.</summary>
    Candidate = 2,

    /// <summary>A preserved historical revision, designated read-only.</summary>
    LegacyReadOnly = 3,
}

/// <summary>
/// One decoded row, addressed by logical sheet and logical column name.
///
/// <para>Logical names are the contract. The physical header text, the column letter and the
/// row offset are schema mechanics that this record has already resolved away, so a caller that
/// reads <c>record["WorkItemId"]</c> keeps working when the physical layout changes.</para>
/// </summary>
public sealed record DevelopmentControlRecord(
    string LogicalSheet,
    int Row,
    IReadOnlyDictionary<string, string> Values)
{
    /// <summary>The value for a logical column, or null when the column is absent or blank.</summary>
    public string? Get(string logicalColumn) =>
        Values.TryGetValue(logicalColumn, out var v) ? v : null;
}

/// <summary>One sheet as it was actually found in the container.</summary>
/// <param name="Present">
/// The sheet has a physical counterpart in this workbook AND that counterpart was locatable.
/// <para>False therefore covers two different causes — the form has no counterpart for this
/// logical sheet at all (<c>AbsentInForm</c>), or the form names one that the container does not
/// contain (<c>DeclaredButMissing</c>). <see cref="Diagnostics"/> is not on this record; the
/// distinguishing text is on the read result's own diagnostics, which name the cause in full.</para>
/// <para><b>W8D-R5 TASK 1 corrected this from the original projection</b>
/// <c>Presence != DeclaredButMissing</c>, which returned TRUE for <c>AbsentInForm</c> — a sheet
/// that is absent by construction, carrying a null <see cref="PhysicalName"/> and zero records,
/// reported as present. That was survivable only for as long as nothing consumed it: the single
/// consumer was <c>LineageUnavailable</c>, and before this task <c>GitLineage</c> had no binding
/// at all, so <c>Sheet("GitLineage")</c> returned null and the contradiction never surfaced.
/// Adding the binding made the frozen forms materialise an <c>AbsentInForm</c> GitLineage read,
/// at which point the old projection would have answered "lineage IS readable" for a legacy
/// workbook that has no lineage sheet — turning a correct refusal into a confident wrong answer.
/// A member named <c>Present</c>, with a null physical name and no header row, cannot mean
/// "absent by construction".</para>
/// </param>
public sealed record DevelopmentControlSheetRead(
    string LogicalName,
    bool Present,
    string? PhysicalName,
    int? HeaderRow,
    bool HeaderRowLocated,
    IReadOnlyList<string> MissingRequiredColumns,
    IReadOnlyList<DevelopmentControlRecord> Records,
    IReadOnlyList<string> DecodeFailures,
    int LastRow)
{
    /// <summary>
    /// W8D-R5 TASK 1. Logical columns this form binds for this logical sheet that did NOT resolve
    /// against the sheet's header row — either because the form carries no counterpart column at
    /// all, or because the form names a header the sheet does not have.
    ///
    /// <para>Both causes answer the same question the same way: a value cannot be read from this
    /// column here. A surface-level availability flag needs exactly that, and it cannot be derived
    /// from <see cref="Records"/>, because a record omits a key whose cell is blank — so
    /// "no row carries it" cannot distinguish an unprojected column from a projected column that
    /// every row leaves empty. Those are the two answers this contract exists to keep apart.</para>
    ///
    /// <para>Supersets <see cref="MissingRequiredColumns"/>: every required-but-missing column
    /// appears here too, alongside the optional and not-applicable ones.</para>
    /// </summary>
    public IReadOnlyList<string> UnprojectedColumns { get; init; } = [];
}

#endregion

/// <summary>
/// W8D-R5 TASK 1. The three states a surface can be in, kept apart because collapsing any two of
/// them produces an answer a caller may act on when it should not.
/// </summary>
public sealed record DevelopmentControlSurfaceCoverage(
    string LogicalSheet,
    bool SheetLocated,
    bool HeaderRowLocated,
    IReadOnlyList<string> UnprojectedColumns,
    int RecordCount)
{
    /// <summary>The form binds this sheet, the sheet is in the container, and its header row was
    /// located. Columns may still be unprojected — see <see cref="UnprojectedColumns"/>.</summary>
    public bool Readable => SheetLocated && HeaderRowLocated;

    /// <summary>A subset of the bound columns that did not resolve. Non-empty does not mean the
    /// surface is unreadable; it means those particular columns cannot be read here.</summary>
    public bool FullyProjected => Readable && UnprojectedColumns.Count == 0;

    /// <summary>
    /// Readable AND carrying at least one row. <b>This is the member to check before reading an
    /// empty lookup result as a fact about the record.</b> On a surface where this is false, an
    /// empty result means the model holds no data for anyone, not that the record has none.
    /// </summary>
    public bool CarriesData => Readable && RecordCount > 0;
}

#region 1 - DevelopmentControlReader

/// <summary>
/// Reads a DevelopmentControl workbook without interpreting its governance.
///
/// <para>This is deliberately the <i>lowest</i> contract: it answers "what does the file
/// say", never "what may I do about it". Authorization is
/// <see cref="IDevelopmentControlWriter"/>'s job, and keeping them apart is what stops a
/// reader from becoming an accidental write path.</para>
/// </summary>
public interface IDevelopmentControlReader
{
    /// <summary>
    /// Decodes a workbook. Never throws on a malformed or unrecognised container - it returns a
    /// result whose <see cref="DevelopmentControlReadResult.Form"/> is
    /// <see cref="DevelopmentControlForm.Unknown"/> and whose diagnostics say why. A reader that
    /// throws cannot be used to <i>diagnose</i> an unreadable authority, which is exactly when
    /// it is most needed.
    /// </summary>
    DevelopmentControlReadResult Read(string workbookPath, DevelopmentControlForm? formHint = null);

    /// <summary>Lowercase hex SHA-256 of the file's bytes, as stored.</summary>
    string Sha256Of(string path);
}

/// <summary>The decoded workbook, plus everything the caller needs to judge whether to trust it.</summary>
public sealed record DevelopmentControlReadResult(
    string Path,
    DevelopmentControlForm Form,
    string Sha256,
    DevelopmentControlAuthority Authority,
    string SchemaId,
    string SchemaVersion,
    IReadOnlyList<DevelopmentControlSheetRead> Sheets,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>Every record across every sheet, in sheet order.</summary>
    public IReadOnlyList<DevelopmentControlRecord> Records { get; init; } = [];

    /// <summary>
    /// Physical sheet names present in the container that NO logical binding consumes.
    ///
    /// <para><b>Why this is on the contract rather than left to the implementation.</b> The V3
    /// model has 26 sheets and not all of them are projected through logical bindings. The
    /// unprojected ones are named here so that "this workbook contains the data and the contract
    /// does not project it" is a <b>checkable</b> statement rather than a silence.</para>
    ///
    /// <para>Without this list a caller receiving an empty
    /// <see cref="IDependencyLineageLookup.LineageOf"/> result cannot tell "this record has no
    /// lineage" from "lineage is not readable through this contract at all". Those are different
    /// answers and only one of them is safe to act on. W8D-R4 TASK 5 found this the hard way;
    /// see W8DR4_DEVELOPER_ADAPTATION.md.</para>
    ///
    /// <para><b>W8D-R5 TASK 1 narrowed this list by three.</b> This paragraph previously named
    /// <c>13_GitLineage</c>, <c>09_ChangeScopes</c> and <c>19_ChangeRequests</c> as its examples.
    /// All three now have bindings and no longer appear here — and the doc was corrected in the
    /// same commit as the bindings, because a comment naming the unprojected sheets is a claim
    /// about the contract, and one left stale reads as evidence that the gap is still open. What
    /// remains is still substantive: <c>11_Reservations</c> holds the lease facts the collision
    /// engine consumes on the other three forms, and <c>17_Evidence</c> holds the acceptance
    /// evidence the new acceptance-state columns only point at.</para>
    /// </summary>
    public IReadOnlyList<string> UnboundSheets { get; init; } = [];

    public DevelopmentControlSheetRead? Sheet(string logicalName) =>
        Sheets.FirstOrDefault(s => string.Equals(s.LogicalName, logicalName, StringComparison.Ordinal));

    /// <summary>
    /// W8D-R5 TASK 1. How much of a logical surface this workbook actually carries, or null when
    /// the form has no binding for that logical sheet at all.
    ///
    /// <para><b>Why this is needed.</b> A surface can be PRESENT, BOUND, CORRECTLY HEADERED and
    /// still carry no rows. The V3 candidate's <c>13_GitLineage</c> is exactly that: 26 headers on
    /// row 4, sound structure, and zero data rows. A caller asking it for a record's lineage gets
    /// an empty list, and an empty list is the same answer it would get for a record that has no
    /// lineage — so before this member, "the model records no lineage for anything" and "this work
    /// item has no lineage" were indistinguishable, and only one of them is safe to act on.</para>
    ///
    /// <para>Expressed as coverage of the SURFACE rather than as a per-lookup flag because the
    /// question is a property of the workbook, not of the lookup, and because the same three
    /// states apply to all seventeen logical sheets.</para>
    /// </summary>
    public DevelopmentControlSurfaceCoverage? Coverage(string logicalSheet)
    {
        var s = Sheet(logicalSheet);
        return s is null
            ? null
            : new DevelopmentControlSurfaceCoverage(
                s.LogicalName, s.Present, s.HeaderRowLocated, s.UnprojectedColumns, s.Records.Count);
    }

    /// <summary>
    /// True when a sheet by this physical name exists in the container, whether or not a logical
    /// binding projects it. The question "is the data there" and the question "can I read it
    /// through this contract" have different answers, and this answers the first one.
    /// </summary>
    public bool HasPhysicalSheet(string physicalName) =>
        Sheets.Any(s => string.Equals(s.PhysicalName, physicalName, StringComparison.OrdinalIgnoreCase))
        || UnboundSheets.Contains(physicalName, StringComparer.OrdinalIgnoreCase);

    public int RecordCount => Sheets.Sum(s => s.Records.Count);

    /// <summary>
    /// True when this result is structurally complete enough to govern. <b>Not</b> the same as
    /// being authoritative - see <see cref="Authority"/>.
    /// </summary>
    public bool MayGovern =>
        Form is not DevelopmentControlForm.Unknown
        && Sheets.All(s => s.MissingRequiredColumns.Count == 0);
}

#endregion

#region 2 - DevelopmentControlWriter

/// <summary>
/// Decides whether a write is permitted, and performs it only under a held reservation.
///
/// <para>The separation between <see cref="AuthoriseWrite"/> and
/// <see cref="IDevelopmentControlReservation"/> is intentional and is the safety property R4-01
/// names: a caller may be told "this write would be permitted" and still be unable to perform it
/// without first acquiring the lock. Authorization is advisory; the lock is the enforcement.</para>
/// </summary>
public interface IDevelopmentControlWriter
{
    /// <summary>
    /// Answers whether a write to <paramref name="workbookPath"/> would be permitted, and why.
    /// Pure: performs no I/O beyond reading the target, and acquires nothing.
    /// </summary>
    DevelopmentControlWriteAuthorisation AuthoriseWrite(
        string workbookPath,
        DevelopmentControlForm intendedForm);

    /// <summary>
    /// Performs a cell write. Refuses - it does not throw - when the reservation is not held,
    /// when authorization fails, or when the target is a refused path class.
    /// </summary>
    DevelopmentControlWriteResult Write(
        IDevelopmentControlReservation reservation,
        DevelopmentControlCellWrite write);
}

/// <summary>Why a write was or was not permitted.</summary>
public enum DevelopmentControlWriteVerdict
{
    Unknown = 0,

    /// <summary>Permitted.</summary>
    Allowed = 1,

    /// <summary>Refused: the workbook declares itself a candidate.</summary>
    RefusedCandidate = 2,

    /// <summary>Refused: the workbook is a preserved historical revision.</summary>
    RefusedLegacyReadOnly = 3,

    /// <summary>Refused: the path is outside every permitted authority root.</summary>
    RefusedPath = 4,

    /// <summary>Refused: the requested form does not match what the file actually is.</summary>
    RefusedFormMismatch = 5,

    /// <summary>Refused: the workbook could not be read far enough to judge.</summary>
    RefusedUnreadable = 6,

    /// <summary>Refused: the schema is not one a writer may govern.</summary>
    RefusedSchema = 7,
}

/// <summary>The authorization decision, with the evidence for it.</summary>
public sealed record DevelopmentControlWriteAuthorisation(
    DevelopmentControlWriteVerdict Verdict,
    bool Allowed,
    DevelopmentControlForm Form,
    string TargetPath,
    string SchemaId,
    string SchemaVersion,
    DevelopmentControlAuthority Authority,
    string Reason);

/// <summary>One cell to write, addressed by logical sheet and logical column.</summary>
public sealed record DevelopmentControlCellWrite(
    string LogicalSheet,
    int Row,
    string LogicalColumn,
    string Value);

/// <summary>The outcome of an attempted write.</summary>
public sealed record DevelopmentControlWriteResult(
    bool Written,
    string Reason,
    string? PreviousValue,
    string? NewValue);

#endregion

#region 3 - Reservation / writer-lock contract

/// <summary>
/// The exclusive writer claim over one workbook.
///
/// <para>Both hosts must contend on the <b>same</b> operating-system object for the same
/// workbook. That is the property W8D-R3 proved identical across 22 cross-host vector inputs,
/// and it is the property that fails silently and dangerously if either host derives the object
/// name differently: two writers would each believe they held the lock.</para>
/// </summary>
public interface IDevelopmentControlReservation : IDisposable
{
    /// <summary>The workbook this reservation covers.</summary>
    string StorePath { get; }

    /// <summary>True while this caller holds the claim.</summary>
    bool Held { get; }

    /// <summary>The lease this caller currently holds.</summary>
    IReservationLease Lease { get; }
}

/// <summary>Acquires writer reservations. The only sanctioned way to obtain one.</summary>
public interface IDevelopmentControlLockService
{
    /// <summary>
    /// Attempts to claim <paramref name="storePath"/>. Never throws on contention - contention
    /// is a normal outcome, reported as <see cref="DevelopmentControlLockOutcome.BusyLive"/> or
    /// <see cref="DevelopmentControlLockOutcome.BusyStaleLive"/>.
    /// </summary>
    DevelopmentControlLockAttempt TryAcquire(
        string storePath,
        string lockDirectory,
        string? owner = null);

    /// <summary>True when some other live process holds the claim.</summary>
    bool IsHeldByAnotherProcess(string storePath, string lockDirectory);

    /// <summary>
    /// The canonical lock identity for a workbook path. Exposed so a host can assert that it
    /// derives the same identity as the other host <b>without</b> acquiring anything.
    /// </summary>
    string LockIdentityFor(string storePath);
}

/// <summary>Why a claim attempt ended as it did.</summary>
public enum DevelopmentControlLockOutcome
{
    Unknown = 0,

    /// <summary>This caller now holds the claim.</summary>
    Acquired = 1,

    /// <summary>A live OS handle is held by another process.</summary>
    BusyLive = 2,

    /// <summary>
    /// No live handle, but the previous holder's lease has not expired. Treated as BUSY, not as
    /// reclaimable: this is what protects a long-running build that emits no heartbeat.
    /// </summary>
    BusyStaleLive = 3,

    /// <summary>No live handle and the lease expired - reclaimable, subject to the guard checks.</summary>
    Reclaimable = 4,

    /// <summary>Reclaimable in principle, but a safety guard refused it.</summary>
    ReclaimBlocked = 5,

    /// <summary>The path is one a writer must never claim.</summary>
    RefusedPath = 6,
}

/// <summary>The result of a claim attempt.</summary>
public sealed record DevelopmentControlLockAttempt(
    DevelopmentControlLockOutcome Outcome,
    string Detail,
    IReadOnlyList<string> Evidence,
    IReservationLease? Holder,
    /// <summary>
    /// The claim this caller now holds, present only when <see cref="Outcome"/> is
    /// <see cref="DevelopmentControlLockOutcome.Acquired"/>. This is the handle a write requires;
    /// it is not obtainable any other way, which is what makes "authorized" and "able to write"
    /// two different states.
    /// </summary>
    IDevelopmentControlReservation? Reservation = null)
{
    public bool Acquired => Outcome == DevelopmentControlLockOutcome.Acquired;

    /// <summary>True when retrying is sensible: a live holder is transient.</summary>
    public bool Retryable =>
        Outcome is DevelopmentControlLockOutcome.BusyLive
                or DevelopmentControlLockOutcome.BusyStaleLive;
}

#endregion

#region 4 - Lease / heartbeat contract

/// <summary>
/// A time-bounded claim with a heartbeat.
///
/// <para>A lease without a heartbeat cannot distinguish "still working" from "died holding the
/// lock", and a reclaim policy that guesses wrong either blocks a live writer or admits a second
/// one. The heartbeat is what makes the distinction observable rather than inferred.</para>
/// </summary>
public interface IReservationLease
{
    string Owner { get; }

    /// <summary>When the claim was taken.</summary>
    DateTimeOffset AcquiredAt { get; }

    /// <summary>When the claim stops being defended if no heartbeat arrives.</summary>
    DateTimeOffset ExpiresAt { get; }

    /// <summary>Host + process identity of the holder, so a reader can tell live from dead.</summary>
    string HostId { get; }

    /// <summary>True when the lease has lapsed.</summary>
    bool Expired { get; }
}

/// <summary>Extends and releases leases. Held by the reservation that owns them.</summary>
public interface ILeaseHeartbeat
{
    /// <summary>Extends the lease. Returns the lease as it now stands.</summary>
    IReservationLease Heartbeat(TimeSpan? extend = null);

    /// <summary>Releases early. Idempotent.</summary>
    IReservationLease Release();
}

#endregion

#region 5 - ChangeScope contract

/// <summary>
/// What a change declares it will touch, and the collision policy over those declarations.
///
/// <para>This is enforcement, not documentation. The point of declaring scope before work starts
/// is that two lanes whose declared scopes collide can be refused <i>before</i> either writes,
/// rather than discovered to have conflicted after both did.</para>
/// </summary>
public interface IChangeScopePolicy
{
    /// <summary>Evaluates declared scopes against each other and returns the collisions found.</summary>
    ChangeScopeCollisionResult Evaluate(IReadOnlyList<ChangeScopeDeclaration> declared);

    /// <summary>True when the declared scopes are mutually compatible.</summary>
    bool AreCompatible(IReadOnlyList<ChangeScopeDeclaration> declared);
}

/// <summary>What kind of resource a scope item names.</summary>
public enum ChangeScopeItemKind
{
    /// <summary>
    /// A single file. Named to avoid colliding with <c>Nexus.ProductCore.Contracts.ScopeKind</c>,
    /// which is a wrapped-string scope-<i>node</i> tag for the product hierarchy and means
    /// something unrelated. Two different concepts must not share one name across a boundary
    /// this file creates.
    /// </summary>
    ExactFile = 0,

    DirectorySubtree = 1,
    Glob = 2,
    ProjectResource = 3,
    PublicContract = 4,
    DatabaseMigration = 5,
    ControlStore = 6,
}

/// <summary>Whether a declaration claims exclusive use or shared read access.</summary>
public enum ChangeScopeAccessMode
{
    /// <summary>This lane will modify the resource.</summary>
    Write = 0,

    /// <summary>This lane will only read the resource.</summary>
    Read = 1,
}

/// <summary>One resource a change declares.</summary>
public sealed record ChangeScopeItem(
    ChangeScopeItemKind Kind,
    ChangeScopeAccessMode AccessMode,
    string Target);

/// <summary>A lane's declared scope and the envelope it belongs to.</summary>
public sealed record ChangeScopeDeclaration(
    string Lane,
    string ChangeId,
    IReadOnlyList<ChangeScopeItem> Items);

/// <summary>How two declarations relate over one resource.</summary>
public enum ChangeScopeCollisionVerdict
{
    /// <summary>No overlap.</summary>
    Disjoint = 0,

    /// <summary>Both only read. Compatible.</summary>
    SharedRead = 1,

    /// <summary>One reads, one writes. Permitted but reported.</summary>
    ReadWriteOverlap = 2,

    /// <summary>Both write. Refused.</summary>
    WriteWriteCollision = 3,

    /// <summary>Overlap exists but intent could not be established. Refused, not guessed.</summary>
    Indeterminate = 4,
}

/// <summary>
/// One detected conflict between two lanes.
///
/// <para><b>Named "Conflict", not "Collision", deliberately.</b> The implementation exposes a
/// static collision <i>engine</i> called <c>ChangeScopeCollision</c>. A contract record of the
/// same name shadows it inside any file that imports both namespaces, and the compiler resolves
/// that ambiguity as an error at best - the first build of this adapter layer failed on exactly
/// that (CS0104). One name for the engine and another for the datum it produces removes the
/// hazard rather than papering over it with an alias at each use site.</para>
/// </summary>
public sealed record ChangeScopeConflict(
    string LeftLane,
    string RightLane,
    string Resource,
    ChangeScopeCollisionVerdict Verdict);

/// <summary>Every conflict between the declared scopes.</summary>
public sealed record ChangeScopeCollisionResult(
    IReadOnlyList<ChangeScopeConflict> Conflicts,
    bool Compatible,
    string Detail);

#endregion

#region 6 - Dependency / lineage lookup contract

/// <summary>
/// Answers what a work item depends on, and where a control record came from in Git history.
///
/// <para>Returned as plain strings rather than resolved domain objects. A host holds foreign
/// identifiers as values; resolving them into its own domain types is the host's business and
/// must not be done here, or this assembly would start encoding one product's domain.</para>
/// </summary>
public interface IDependencyLineageLookup
{
    /// <summary>Declared dependencies of a work item, as address strings.</summary>
    IReadOnlyList<DevelopmentControlDependency> DependenciesOf(string workItemId);

    /// <summary>Work items that declare a dependency on <paramref name="workItemId"/>.</summary>
    IReadOnlyList<DevelopmentControlDependency> DependentsOf(string workItemId);

    /// <summary>
    /// Git lineage for a control record: which repository, base, branch and commits have carried
    /// it. Empty when the workbook carries no lineage for the record - which is not the same as
    /// "the record has no history", and callers must not read it that way. Check
    /// <see cref="LineageUnavailable"/> first.
    /// </summary>
    IReadOnlyList<DevelopmentControlGitLineage> LineageOf(string logicalSheet, string recordKey);

    /// <summary>
    /// Declared change scope for a work item, one entry per scope row. Empty when the workbook
    /// declares no scope for it - check <see cref="ChangeScopesUnavailable"/> first.
    /// </summary>
    IReadOnlyList<DevelopmentControlScopeRow> ScopesOf(string workItemId);

    /// <summary>
    /// Core Change Requests raised by a work item. Empty when none are declared - check
    /// <see cref="ChangeRequestsUnavailable"/> first.
    /// </summary>
    IReadOnlyList<DevelopmentControlChangeRequest> ChangeRequestsOf(string workItemId);

    /// <summary>
    /// Acceptance state for a work item, or null when the workbook declares none. Null is
    /// "not declared"; check <see cref="AcceptanceUnavailable"/> for "not readable", which is the
    /// different and more dangerous answer.
    /// </summary>
    DevelopmentControlAcceptance? AcceptanceOf(string workItemId);

    /// <summary>
    /// True when this workbook has no readable Git-lineage surface, so lineage lookups are
    /// UNAVAILABLE rather than empty.
    ///
    /// <para>The V3 candidate has <c>13_GitLineage</c> and this is false. On the three frozen
    /// forms the logical sheet is absent by construction and this is true. W8D-R4 measured the V3
    /// candidate in the unavailable state because no binding existed; W8D-R5 TASK 1 added one, and
    /// the property flips without an edit here — which is what it was written to do.</para>
    /// </summary>
    bool LineageUnavailable { get; }

    /// <summary>True when this workbook has no readable change-scope surface.</summary>
    bool ChangeScopesUnavailable { get; }

    /// <summary>True when this workbook has no readable Core Change Request surface.</summary>
    bool ChangeRequestsUnavailable { get; }

    /// <summary>
    /// True when this workbook has no readable acceptance-state surface. Unlike the three above
    /// this is not a whole sheet: acceptance state lives in three columns of the work-item sheet,
    /// so a form that has the sheet but not the columns is unavailable too.
    /// </summary>
    bool AcceptanceUnavailable { get; }
}

/// <summary>One declared dependency edge.</summary>
public sealed record DevelopmentControlDependency(
    string FromWorkItemId,
    string ToWorkItemId,
    string Kind,
    string Status);

/// <summary>
/// One Git-lineage row: where a work item's work actually lives in version control.
///
/// <para><b>Corrected by W8D-R5 TASK 1.</b> This record previously carried a
/// <c>BlobSha256</c> member. No binding ever projected it, and <c>13_GitLineage</c> — the only
/// sheet that carries lineage — has no such column in any of its 26 columns A..Z. The member was
/// therefore never a projection of anything; it was a field the record shape asserted and no
/// workbook could satisfy, and the adapter reading it returned empty for every input on every
/// form without ever failing. A record member with no source is worse than a missing member,
/// because it reads as a fact that was checked.</para>
///
/// <para>The workbook-blob-at-commit that member appears to have been reaching for is
/// <b>UNKNOWN</b> for every record in every form — the V3 model does not carry it, and this
/// contract will not synthesise it from the file on disk, which is the workbook's hash TODAY and
/// not at the commit named here. Today's hash presented as history would be a fabricated
/// provenance claim.</para>
/// </summary>
public sealed record DevelopmentControlGitLineage(
    string LineageId,
    string WorkItemId,
    string ChangeId,
    string ReservationId,
    string RepositoryId,
    string Branch,
    string WorktreePath,
    string BaseSha,
    string CommitSha,
    string PullRequest,
    string IntegrationSha,
    DateTimeOffset? RecordedAt,
    string IsCurrentText,
    string Note)
{
    /// <summary>
    /// Whether this is the current lineage row. Nullable, and null is "the cell did not say" —
    /// the measured V3 vocabulary is <c>Yes</c>, a spelling <c>bool.Parse</c> rejects, and an
    /// empty cell is rejected by both. A non-nullable bool would have to pick a value for those,
    /// and either pick is a claim the workbook did not make.
    /// </summary>
    public bool? IsCurrent => DevelopmentControlFlagVocabulary.Parse(IsCurrentText);

    /// <summary>
    /// True when this row names the commit that delivered the work. A row may carry a base and a
    /// branch without naming any commit — that is a declared intent, not a delivery — and the two
    /// must not read alike.
    /// </summary>
    public bool NamesCommit => !string.IsNullOrWhiteSpace(CommitSha);

    /// <summary>
    /// True when the row names the SHA the work was integrated at. This is the rollback point for
    /// TASK 7's purposes, and it is deliberately distinct from <see cref="CommitSha"/>: work can
    /// be committed on a branch and never integrated.
    /// </summary>
    public bool NamesIntegration => !string.IsNullOrWhiteSpace(IntegrationSha);
}

/// <summary>
/// One change-scope row: a single resource a work item declares it will touch.
///
/// <para>The two enums are resolved through <see cref="DevelopmentControlScopeVocabulary"/> and
/// are NULLABLE for a reason. A scope row whose <c>ItemType</c> or <c>Access</c> text is not a
/// member of the vocabulary must surface as unparsed, never as a default. Defaulting an
/// unrecognised item type to <see cref="ChangeScopeItemKind.ExactFile"/> would narrow a broad
/// declaration into a narrow one, and defaulting access to
/// <see cref="ChangeScopeAccessMode.Write"/> would do the same in the other direction — either
/// one silently changes what a collision check compares.</para>
/// </summary>
public sealed record DevelopmentControlScopeRow(
    string ScopeRecordId,
    string WorkItemId,
    string ScopeClass,
    string ItemTypeText,
    string Target,
    string AccessText,
    string ReviewState,
    string IsCurrentText,
    string Note)
{
    /// <summary>Whether this is the current scope row. Nullable for the same reason as
    /// <see cref="DevelopmentControlGitLineage.IsCurrent"/>.</summary>
    public bool? IsCurrent => DevelopmentControlFlagVocabulary.Parse(IsCurrentText);

    /// <summary>The declared item kind, or null when <see cref="ItemTypeText"/> is not a member
    /// of <see cref="ChangeScopeItemKind"/>. Null is "unrecognised", not "unconstrained".</summary>
    public ChangeScopeItemKind? ItemType => DevelopmentControlScopeVocabulary.ParseItemKind(ItemTypeText);

    /// <summary>The declared access mode, or null when <see cref="AccessText"/> is not a member
    /// of <see cref="ChangeScopeAccessMode"/>.</summary>
    public ChangeScopeAccessMode? Access => DevelopmentControlScopeVocabulary.ParseAccessMode(AccessText);

    /// <summary>True when both vocabulary fields parsed. A row that is not
    /// <see cref="WellFormed"/> must not be fed to a collision comparison.</summary>
    public bool WellFormed => ItemType is not null && Access is not null;
}

/// <summary>
/// One Core Change Request: what a product head asked the Platform for, under which policy, and
/// where it ended up.
///
/// <para><see cref="Status"/> and <see cref="LegacyStatusText"/> are both carried raw and are
/// deliberately NOT reconciled here. W8D-R5 TASK 5 exists because the pre-migration wording and
/// the V3 status disagree on three records, and a contract that normalised the two into one
/// agreed value would destroy the evidence the reconciliation is built on. No derived
/// "disagrees" helper is offered either: any such helper is a heuristic over free text, and a
/// heuristic here would manufacture a conflict signal in the one place the directive requires
/// evidence rather than inference.</para>
/// </summary>
public sealed record DevelopmentControlChangeRequest(
    string RequestId,
    string RequestType,
    string Destination,
    string RequestingWorkId,
    string RequestingHead,
    string CapabilityRequested,
    string Purpose,
    string ContextRefs,
    string DataClassification,
    string ExecutionPolicy,
    string ToolPermissionProfile,
    string Status,
    string PlatformChangeRequestId,
    string HandbackWorkId,
    string LegacyStatusText,
    string IsCurrentText,
    string Note)
{
    /// <summary>Whether this is the current request row. Nullable for the same reason as
    /// <see cref="DevelopmentControlGitLineage.IsCurrent"/>.</summary>
    public bool? IsCurrent => DevelopmentControlFlagVocabulary.Parse(IsCurrentText);
}

/// <summary>
/// Acceptance state for one work item: the three V3 columns that were present in the workbook and
/// unprojected before W8D-R5 TASK 1.
/// </summary>
public sealed record DevelopmentControlAcceptance(
    string WorkItemId,
    string AcceptanceCriteria,
    string EvidenceRequired,
    string ReadinessState)
{
    /// <summary>
    /// True when the work item states what would make it acceptable. False is a real finding —
    /// TASK 12 asks whether every current work item carries acceptance evidence, and a work item
    /// with no criteria is one that cannot be accepted, only asserted complete.
    /// </summary>
    public bool CriteriaDeclared => !string.IsNullOrWhiteSpace(AcceptanceCriteria);

    /// <summary>True when the work item names the evidence that would discharge those criteria.</summary>
    public bool EvidenceNamed => !string.IsNullOrWhiteSpace(EvidenceRequired);
}

/// <summary>
/// Parses the two V3 scope vocabularies. Separate from the record so both hosts resolve them
/// through one implementation rather than each writing its own <c>Enum.TryParse</c> — with
/// different casing rules, or with a fallback default on one side and not the other.
/// </summary>
/// <remarks>
/// <b>W8D-R5 TASK 1 measured the V3 scope vocabulary against this enum and they do not
/// intersect.</b> Distinct <c>ItemType</c> values in <c>09_ChangeScopes</c>, over 223 data rows,
/// are <c>CONTRACT</c> (36), <c>DB_CONTEXT</c> (67), <c>FILE_GLOB</c> (9) and <c>PROJECT</c>
/// (111); <c>Access</c> is <c>WRITE</c> on all 223. The enum's members are
/// <see cref="ChangeScopeItemKind.ExactFile"/>, <see cref="ChangeScopeItemKind.DirectorySubtree"/>,
/// <see cref="ChangeScopeItemKind.Glob"/>, <see cref="ChangeScopeItemKind.ProjectResource"/>,
/// <see cref="ChangeScopeItemKind.PublicContract"/>, <see cref="ChangeScopeItemKind.DatabaseMigration"/>
/// and <see cref="ChangeScopeItemKind.ControlStore"/>. <b>Zero of the four V3 values is a member.</b>
///
/// <para>Those parsers therefore return null for every scope row the V3 candidate carries, and
/// that is the CORRECT answer rather than a defect being introduced: the raw text genuinely is not
/// a member of the enum, and a parser that mapped it anyway would be inventing a correspondence
/// the model does not declare. The bridge between the two vocabularies is a semantic decision —
/// <c>FILE_GLOB</c>→<c>Glob</c> and <c>PROJECT</c>→<c>ProjectResource</c> are near-certain, while
/// <c>DB_CONTEXT</c> could be either <c>DatabaseMigration</c> or <c>ProjectResource</c> and
/// <c>CONTRACT</c> does not say whether the contract is public — so it is raised as an Owner
/// decision rather than applied here. See W8DR5_CHANGESCOPE_ACCEPTANCE_TESTS.md.</para>
///
/// <para><b>Consequence a caller must not misread:</b> <see cref="DevelopmentControlScopeRow.WellFormed"/>
/// being false on every row means "the vocabulary bridge is not established", NOT "no scope is
/// declared". The scope rows are present and fully readable. Readability is
/// <see cref="DevelopmentControlSurfaceCoverage"/>, which is a different question.</para>
/// </remarks>
public static class DevelopmentControlScopeVocabulary
{
    /// <summary>
    /// Parses <c>ItemType</c> case-insensitively. Returns null for anything that is not a member,
    /// including empty text. There is no fallback member by design.
    /// </summary>
    public static ChangeScopeItemKind? ParseItemKind(string? text) =>
        Enum.TryParse<ChangeScopeItemKind>(text?.Trim(), ignoreCase: true, out var kind)
        && Enum.IsDefined(kind)
            ? kind
            : null;

    /// <summary>
    /// Parses <c>Access</c> case-insensitively. Returns null for anything that is not a member,
    /// including empty text. There is no fallback member by design.
    /// </summary>
    public static ChangeScopeAccessMode? ParseAccessMode(string? text) =>
        Enum.TryParse<ChangeScopeAccessMode>(text?.Trim(), ignoreCase: true, out var mode)
        && Enum.IsDefined(mode)
            ? mode
            : null;
}

/// <summary>
/// Parses V3's <c>IsCurrent</c> flags into a tri-state.
/// </summary>
/// <remarks>
/// The accepted spellings are declared rather than inferred, and each one is there because it was
/// measured or because it is the spelling a workbook author reaches for next. <b>Measured:</b>
/// <c>Yes</c> — 223 rows on <c>09_ChangeScopes</c> and 11 rows on <c>19_ChangeRequests</c>, and no
/// other value on either. <b>Declared:</b> <c>No</c> as its complement, and <c>True</c>/<c>False</c>
/// because the boolean-shaped spelling already appears elsewhere in this model.
///
/// <para>Anything else — including a blank cell — returns null, and a numeric string is
/// deliberately NOT accepted even though <c>Enum.TryParse</c> would take <c>"1"</c>: <c>1</c> is
/// not a declared value of <c>Yes</c>, and accepting it would let an unrecognised cell read as a
/// confident true.</para>
/// </remarks>
public static class DevelopmentControlFlagVocabulary
{
    /// <summary>True, false, or null when the text is not a recognised boolean spelling.</summary>
    public static bool? Parse(string? text)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        if (t.Equals("Yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.Equals("No", StringComparison.OrdinalIgnoreCase)) return false;
        if (t.Equals("True", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.Equals("False", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }
}

#endregion
