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
public sealed record DevelopmentControlSheetRead(
    string LogicalName,
    bool Present,
    string? PhysicalName,
    int? HeaderRow,
    bool HeaderRowLocated,
    IReadOnlyList<string> MissingRequiredColumns,
    IReadOnlyList<DevelopmentControlRecord> Records,
    IReadOnlyList<string> DecodeFailures,
    int LastRow);

#endregion

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
    /// model has 26 sheets and only 8 of them are projected through logical bindings. Three of the
    /// unprojected ones are not incidental - <c>13_GitLineage</c>, <c>09_ChangeScopes</c> and
    /// <c>19_ChangeRequests</c> carry exactly the lineage, scope and change-request facts a caller
    /// most wants - and they are named here so that "this workbook contains the data and the
    /// contract does not project it" is a <b>checkable</b> statement rather than a silence.</para>
    ///
    /// <para>Without this list a caller receiving an empty
    /// <see cref="IDependencyLineageLookup.LineageOf"/> result cannot tell "this record has no
    /// lineage" from "lineage is not readable through this contract at all". Those are different
    /// answers and only one of them is safe to act on. W8D-R4 TASK 5 found this the hard way;
    /// see W8DR4_DEVELOPER_ADAPTATION.md.</para>
    /// </summary>
    public IReadOnlyList<string> UnboundSheets { get; init; } = [];

    public DevelopmentControlSheetRead? Sheet(string logicalName) =>
        Sheets.FirstOrDefault(s => string.Equals(s.LogicalName, logicalName, StringComparison.Ordinal));

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
    /// Git lineage for a control record: the commits and blobs that have carried it.
    /// Empty when the workbook carries no lineage for the record - which is not the same as
    /// "the record has no history", and callers must not read it that way.
    /// </summary>
    IReadOnlyList<DevelopmentControlLineageEntry> LineageOf(string logicalSheet, string recordKey);

    /// <summary>
    /// True when the lineage sheet is present but not bound to a resolvable schema, so lineage
    /// lookups for this workbook are unavailable rather than empty. The V3 candidate is in this
    /// state for <c>13_GitLineage</c>.
    /// </summary>
    bool LineageUnavailable { get; }
}

/// <summary>One declared dependency edge.</summary>
public sealed record DevelopmentControlDependency(
    string FromWorkItemId,
    string ToWorkItemId,
    string Kind,
    string Status);

/// <summary>One point in a control record's Git history.</summary>
public sealed record DevelopmentControlLineageEntry(
    string Commit,
    string BlobSha256,
    DateTimeOffset? RecordedAt,
    string Note);

#endregion
