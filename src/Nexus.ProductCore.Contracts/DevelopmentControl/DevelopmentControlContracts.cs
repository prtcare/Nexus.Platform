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

    /// <summary>
    /// TASK 4. Where this record came from, or null when the form carries no migration envelope.
    ///
    /// <para>Null is a fact about the FORM, not about the record: the three frozen forms have no
    /// envelope columns, so provenance is not merely unrecorded there, it is not representable.
    /// Callers that need to distinguish "this form has no envelope" from "this record's envelope is
    /// blank" should test this property for null first — a non-null result whose
    /// <see cref="DevelopmentControlProvenance.SourceForm"/> is blank is a governed V3 record whose
    /// migration did not record an origin, which is exactly the condition
    /// <see cref="DevelopmentControlProvenance.IsWellFormed"/> refuses to call well-formed.</para>
    /// </summary>
    public DevelopmentControlProvenance? Provenance =>
        Get(DevelopmentControlEnvelopeColumns.SourceForm) is null
            ? null
            : new DevelopmentControlProvenance(
                Get(DevelopmentControlEnvelopeColumns.SourceForm) ?? "",
                Get(DevelopmentControlEnvelopeColumns.SourceWorkbook) ?? "",
                Get(DevelopmentControlEnvelopeColumns.SourceWorkbookHash) ?? "",
                Get(DevelopmentControlEnvelopeColumns.SourceSheet) ?? "",
                Get(DevelopmentControlEnvelopeColumns.SourceRecordId) ?? "",
                Get(DevelopmentControlEnvelopeColumns.SourceRevision) ?? "",
                Get(DevelopmentControlEnvelopeColumns.SourceArchitectureVersion) ?? "",
                Get(DevelopmentControlEnvelopeColumns.MigrationTimestamp) ?? "",
                Get(DevelopmentControlEnvelopeColumns.MigrationTransformation) ?? "");

    /// <summary>
    /// TASK 4. This record's position in the append-only trail, or null when the form carries no
    /// governance envelope.
    ///
    /// <para><see cref="DevelopmentControlEnvelope.IsCurrent"/> stays <c>bool?</c> here for the same
    /// reason <c>DevelopmentControlScopeRow.IsCurrent</c> does: a value the flag vocabulary does not
    /// recognise must surface as unparsed, never defaulted. Defaulting it to false would report a
    /// live record as superseded, and defaulting it to true would report a superseded record as
    /// current — and on a trail whose entire purpose is answering "which version is in force", the
    /// second error is the one that loses work.</para>
    /// </summary>
    public DevelopmentControlEnvelope? Envelope =>
        Get(DevelopmentControlEnvelopeColumns.RecordVersion) is null
               && Get(DevelopmentControlEnvelopeColumns.IsCurrent) is null
            ? null
            : new DevelopmentControlEnvelope(
                Get(DevelopmentControlEnvelopeColumns.RecordVersion) ?? "",
                DevelopmentControlFlagVocabulary.Parse(Get(DevelopmentControlEnvelopeColumns.IsCurrent)),
                DateTimeOffset.TryParse(
                    Get(DevelopmentControlEnvelopeColumns.EffectiveFrom),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var effectiveFrom)
                    ? effectiveFrom
                    : null,
                Get(DevelopmentControlEnvelopeColumns.ChangeId) ?? "",
                Get(DevelopmentControlEnvelopeColumns.SupersedesVersion) ?? "");
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

    /// <summary>
    /// TASK 2. Appends ONE record to a sheet that already exists, through the same reservation and
    /// the same lock as <see cref="Write"/>.
    ///
    /// <para>Refuses - never throws - on every condition it cannot satisfy, and
    /// <see cref="DevelopmentControlAppendResult.Reason"/> names which one. The refusals are the
    /// point of the operation: this is the only path by which a new governance record enters the
    /// authoritative workbook, so every way it could enter wrongly is a way it must decline.</para>
    ///
    /// <para><b>Why this is not "Write with a bigger payload".</b> A cell write targets a cell the
    /// caller has already located, and its failure mode is writing the wrong value into a row that
    /// exists. An append brings a row into existence, so it additionally has to decide what the
    /// row's structure is: which envelope values it carries, whether its identity collides, whether
    /// the sheet is one that may take records at all, and whether the declared change scope covers
    /// the store. Those decisions are made here, under the held lock, against a read taken under
    /// that same lock.</para>
    /// </summary>
    DevelopmentControlAppendResult Append(
        IDevelopmentControlReservation reservation,
        DevelopmentControlAppendRecord record);
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

#region 2b - Append envelope and the append contract

/// <summary>
/// The nine-column MIGRATION ENVELOPE a V3 record carries: which legacy artifact the record came
/// from, and the exact cell within it.
///
/// <para><b>Why this is one cross-sheet type rather than twenty-six column bindings.</b> These
/// nine headers appear with identical spelling on every V3 sheet that has an envelope, and they
/// are always the LAST nine columns. The reader's own binding table records the decision not to
/// bind them per logical sheet (<c>WorkbookCompatibilityReader.Columns</c>, the
/// <c>GitLineage</c> note) and names the correct shape instead: "a projection that wants
/// provenance wants it for every record, not for lineage records only. That is one cross-sheet
/// envelope projection". This record is that projection.</para>
///
/// <para><b>Provenance is a claim, and a claim that cannot be checked is not evidence.</b>
/// <see cref="IsWellFormed"/> requires the claim to be internally consistent: a record that
/// asserts a legacy origin must name the artifact and the cell it came from, and a record that
/// asserts a V3-native origin must NOT name one, because there is no legacy cell to point at.
/// Both halves matter. A legacy claim with a blank source is unfalsifiable — nothing can
/// contradict it, so it cannot support a reconciliation decision. A native record naming a legacy
/// cell is worse: it points a future reader at a cell that does not contain the record they are
/// holding.</para>
/// </summary>
public sealed record DevelopmentControlProvenance(
    string SourceForm,
    string SourceWorkbook,
    string SourceWorkbookHash,
    string SourceSheet,
    string SourceRecordId,
    string SourceRevision,
    string SourceArchitectureVersion,
    string MigrationTimestamp,
    string MigrationTransformation)
{
    /// <summary>
    /// The <see cref="SourceForm"/> value for a record created in V3, with no legacy source.
    ///
    /// <para>The measured V3 vocabulary is <c>L2</c>, <c>V2A</c> and <c>V2B</c> only — 2040 data
    /// rows, zero natives. A natively-created record therefore has no existing token to reuse, and
    /// leaving the column blank is not an option: R5-01 forbids an implicit disposition, and a
    /// blank <c>SourceForm</c> does not read as "native", it reads as "the migration did not fill
    /// this in". <c>V3N</c> is declared here, once, so the value the writer emits and the value a
    /// reader tests for cannot drift apart.</para>
    /// </summary>
    public const string NativeSourceForm = "V3N";

    /// <summary>The legacy form tokens this estate's migration actually used. A V3-native value is
    /// deliberately NOT a member: <see cref="NativeSourceForm"/> is a different kind of claim.</summary>
    public static readonly IReadOnlyList<string> LegacySourceForms = ["L2", "V2A", "V2B"];

    /// <summary>True when this record claims to have been created in V3 with no legacy source.</summary>
    public bool IsNative => string.Equals(SourceForm, NativeSourceForm, StringComparison.Ordinal);

    /// <summary>
    /// True when the nine values are internally consistent as a provenance CLAIM. See the type
    /// doc: the two failure directions are a legacy claim with no source, and a native claim that
    /// names one.
    /// </summary>
    public bool IsWellFormed(out string reason)
    {
        if (string.IsNullOrWhiteSpace(SourceForm))
        {
            reason = "SourceForm is blank. A blank provenance is not 'native' — it is a migration "
                   + "that did not record where the record came from, and this estate forbids an "
                   + "implicit disposition. State either a legacy form ("
                   + string.Join(", ", LegacySourceForms) + ") or "
                   + $"'{NativeSourceForm}' for a record created in V3.";
            return false;
        }

        if (IsNative)
        {
            var named = new List<string>();
            if (!string.IsNullOrWhiteSpace(SourceWorkbook)) named.Add(nameof(SourceWorkbook));
            if (!string.IsNullOrWhiteSpace(SourceSheet)) named.Add(nameof(SourceSheet));
            if (!string.IsNullOrWhiteSpace(SourceRecordId)) named.Add(nameof(SourceRecordId));

            if (named.Count > 0)
            {
                reason = $"SourceForm is '{NativeSourceForm}' (created in V3, no legacy source) but "
                       + $"{string.Join(", ", named)} name a legacy origin. A native record that "
                       + "points at a legacy cell directs a reader to a cell that does not carry it.";
                return false;
            }

            reason = "";
            return true;
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(SourceWorkbook)) missing.Add(nameof(SourceWorkbook));
        if (string.IsNullOrWhiteSpace(SourceSheet)) missing.Add(nameof(SourceSheet));
        if (string.IsNullOrWhiteSpace(SourceRecordId)) missing.Add(nameof(SourceRecordId));

        if (missing.Count > 0)
        {
            reason = $"SourceForm is '{SourceForm}' but {string.Join(", ", missing)} are blank. A "
                   + "legacy claim that does not name the artifact and the record it came from "
                   + "cannot be checked against that artifact, so it cannot support a "
                   + "reconciliation decision.";
            return false;
        }

        reason = "";
        return true;
    }

    /// <summary>
    /// Provenance for a record created in V3. Every legacy-locating field is blank BY RULE — see
    /// <see cref="IsWellFormed"/> — so this factory and that rule cannot disagree.
    /// </summary>
    public static DevelopmentControlProvenance Native(DateTimeOffset at, string transformation) =>
        new(NativeSourceForm, "", "", "", "", "", "",
            at.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            transformation);

    /// <summary>Provenance for a record carried over from a named legacy artifact.</summary>
    public static DevelopmentControlProvenance FromLegacy(
        string sourceForm, string workbook, string workbookHash, string sheet, string sourceRecordId,
        string revision, string architectureVersion, string transformation,
        DateTimeOffset at) =>
        new(sourceForm, workbook, workbookHash, sheet, sourceRecordId, revision, architectureVersion,
            at.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            transformation);
}

/// <summary>
/// The five-column GOVERNANCE ENVELOPE — a record's position in the append-only trail
/// (<c>RecordVersion | IsCurrent | EffectiveFrom | ChangeId | SupersedesVersion</c>).
///
/// <para>The first four are WRITER-OWNED on append: a caller cannot set them through
/// <see cref="DevelopmentControlAppendRecord.Values"/>, because the whole point of the envelope is
/// that the writing component, not the caller, states where the record sits in the trail. Only
/// <see cref="SupersedesVersion"/> is caller-supplied, because only the caller knows whether this
/// record replaces an earlier one.</para>
/// </summary>
public sealed record DevelopmentControlEnvelope(
    string RecordVersion,
    bool? IsCurrent,
    DateTimeOffset? EffectiveFrom,
    string ChangeId,
    string SupersedesVersion);

/// <summary>
/// The logical names of the two envelopes, declared once.
///
/// <para><b>Why a class of constants rather than literals.</b> The reader projects these names, the
/// writer resolves them to physical columns, and a caller reads them back through
/// <see cref="DevelopmentControlRecord"/>. Three consumers, one vocabulary — and this estate has
/// already been bitten twice by a name that resolved on one side and not the other (the W8D-R4
/// TASK 5 lookup that queried five logical names no binding declares, and the W8D-R5 TASK 1 lineage
/// read that queried three more). Compiling against a constant turns the next such mismatch into a
/// build failure instead of a lookup that returns empty for every input, silently, forever.</para>
///
/// <para><b><see cref="ChangeId"/> is spelled <c>EnvelopeChangeId</c>.</b> That is the name the
/// reader's binding table already uses for the envelope's originating change on the three sheets
/// that bind it, and the projection follows the binding rather than the other way round. On a sheet
/// whose business key is also called <c>ChangeId</c>, the two must not share a name.</para>
/// </summary>
public static class DevelopmentControlEnvelopeColumns
{
    // The governance envelope — the record's position in the append-only trail.
    public const string RecordVersion = "RecordVersion";
    public const string IsCurrent = "IsCurrent";
    public const string EffectiveFrom = "EffectiveFrom";
    public const string ChangeId = "EnvelopeChangeId";
    public const string SupersedesVersion = "SupersedesVersion";

    // The migration envelope — where the record came from.
    public const string SourceForm = "SourceForm";
    public const string SourceWorkbook = "SourceWorkbook";
    public const string SourceWorkbookHash = "SourceWorkbookHash";
    public const string SourceSheet = "SourceSheet";
    public const string SourceRecordId = "SourceRecordId";
    public const string SourceRevision = "SourceRevision";
    public const string SourceArchitectureVersion = "SourceArchitectureVersion";
    public const string MigrationTimestamp = "MigrationTimestamp";
    public const string MigrationTransformation = "MigrationTransformation";

    /// <summary>The five governance columns, in the order the form lays them out.</summary>
    public static readonly IReadOnlyList<string> Governance =
        [RecordVersion, IsCurrent, EffectiveFrom, ChangeId, SupersedesVersion];

    /// <summary>The nine migration columns, in the order the form lays them out.</summary>
    public static readonly IReadOnlyList<string> Migration =
    [
        SourceForm, SourceWorkbook, SourceWorkbookHash, SourceSheet, SourceRecordId,
        SourceRevision, SourceArchitectureVersion, MigrationTimestamp, MigrationTransformation,
    ];

    /// <summary>Every column either envelope contributes.</summary>
    public static readonly IReadOnlyList<string> All = [.. Governance, .. Migration];
}

/// <summary>
/// TASK 2. Append one record to an EXISTING sheet of a DevelopmentControl workbook.
///
/// <para><b>Logical, not physical.</b> The sheet is named by its logical binding name and the
/// values are keyed by logical column name, exactly as <see cref="DevelopmentControlCellWrite"/>
/// does. No row number appears anywhere in this type: which row a record lands on is a property of
/// the structure at the instant of the write, and a caller that supplied one would be asserting a
/// fact it cannot know. The writer computes the row, under the held lock, and reports it back in
/// <see cref="DevelopmentControlAppendResult.Row"/>.</para>
///
/// <para><b>The workbook must already have the table.</b> This type names a sheet and a set of
/// columns that must ALREADY EXIST in the form. It cannot create a sheet, cannot create a column,
/// and cannot create the envelope — see <see cref="DevelopmentControlCellWriter"/>'s
/// <c>AppendRow</c> for why the writer refuses rather than inventing structure. TASK 1 requires the
/// writer not gain arbitrary sheet-creation capability; the way that is enforced is that every
/// target is resolved against bindings and headers the workbook already declares, and an
/// unresolved one is a refusal.</para>
/// </summary>
/// <param name="LogicalSheet">Logical name of the target sheet, which must already be present.</param>
/// <param name="IdentityColumn">
/// The logical column that carries this record's IMMUTABLE identity on that sheet — <c>LineageId</c>
/// on GitLineage, <c>ScopeRecordId</c> on ChangeScopes, <c>RequestId</c> on ChangeRequests, and so
/// on.
///
/// <para><b>Declared, not inferred.</b> The binding table does not mark which column is a sheet's
/// key, and the one field that looked like a candidate does not work: <c>Required</c> is true for
/// <c>LineageId</c> and <c>ScopeRecordId</c> but FALSE for <c>DecisionId</c>, so inferring identity
/// from it would silently skip the duplicate check on some sheets while appearing to run it on
/// others. A duplicate check that does not run is worse than none, because it is reported as
/// having passed. Requiring the caller to name the column makes the check a stated precondition,
/// and lets the writer refuse by name when the declared column is not one the sheet carries.</para>
/// </param>
/// <param name="ChangeId">
/// The change authorising this append. Written into the governance envelope's originating-change
/// column, which is the envelope's own <c>ChangeId</c> — not any business <c>ChangeId</c> the sheet
/// may separately carry.
/// </param>
/// <param name="Values">
/// Logical column name to value, for the columns this record populates. Envelope columns are
/// rejected here rather than merged: see <see cref="DevelopmentControlEnvelope"/>.
/// </param>
/// <param name="Provenance">
/// The nine migration-envelope values. Must be internally consistent — see
/// <see cref="DevelopmentControlProvenance.IsWellFormed"/>.
/// </param>
/// <param name="DeclaredScope">
/// The scope this change declares. The append refuses unless it covers this control store for
/// write, which is how "obey ChangeScope" is enforced rather than asserted.
/// </param>
public sealed record DevelopmentControlAppendRecord(
    string LogicalSheet,
    string IdentityColumn,
    string ChangeId,
    IReadOnlyDictionary<string, string> Values,
    DevelopmentControlProvenance Provenance,
    ChangeScopeDeclaration DeclaredScope)
{
    /// <summary>
    /// The record version this append writes. Defaults to <c>"1"</c>, which is the only
    /// <c>RecordVersion</c> value the V3 candidate carries across all 2040 data rows — so a
    /// promoted default is the measured norm rather than a guess. A superseding append states its
    /// own.
    /// </summary>
    public string RecordVersion { get; init; } = "1";

    /// <summary>The record this one replaces, or blank when it replaces nothing.</summary>
    public string SupersedesVersion { get; init; } = "";

    /// <summary>
    /// When the record takes effect. Null means "now, at the instant of the write", which the
    /// writer stamps — it is the only component that knows when the write actually happened.
    /// </summary>
    public DateTimeOffset? EffectiveFrom { get; init; }
}

/// <summary>The outcome of an attempted append. Refusals name the condition, never a generic
/// failure: the caller's remedy differs per condition, and a bare failure leaves them unable to
/// tell "the workbook moved" from "I spelled the column wrong".</summary>
public sealed record DevelopmentControlAppendResult(
    bool Appended,
    string Reason,
    int? Row,
    string? RecordKey,
    DevelopmentControlProvenance? Provenance,
    DevelopmentControlEnvelope? Envelope)
{
    /// <summary>
    /// Whether the declaration that authorised this append actually covered this control store.
    ///
    /// <para><b>Why this is a typed value on the result and not only a sentence in
    /// <see cref="Reason"/>.</b> W8D-R5 recorded a cutover blocker as "SCOPE_CHANGE_REQUIRED is
    /// absent" — the refusal that says <i>your declared scope does not cover this change, so amend
    /// the scope before modifying it</i>. The condition was already enforced (the four refusal
    /// paths below reach it), but every one of them expressed it as English prose, so a consumer
    /// could not branch on it: a host wanting to distinguish "re-declare your scope" from "the
    /// workbook moved" had to match on message text, which is the string-comparison coupling the
    /// rest of this contract exists to avoid. The legacy decision vocabulary this replaces is a
    /// two-value pair — <c>CONTINUE</c> / <c>SCOPE_CHANGE_REQUIRED</c>, emitted per file by Forge's
    /// dependency-lineage resolver — and this property is that pair, spelled as a value.</para>
    ///
    /// <para><b>The four refusal causes stay collapsed into one verdict on purpose.</b> No
    /// declaration at all, a declaration with no <see cref="ChangeScopeItemKind.ControlStore"/>
    /// item, a ControlStore item declared <see cref="ChangeScopeAccessMode.Read"/>, and a
    /// ControlStore item naming a <i>different</i> store are four distinct causes with four distinct
    /// remedies, and they are four distinct sentences in <see cref="Reason"/>. They share one
    /// verdict because they share one <i>decision</i>: the caller's next move in all four is to
    /// amend the declared scope. Splitting them into four verdict members would invent a vocabulary
    /// no legacy artefact defines.</para>
    /// </summary>
    public ChangeScopeContainmentVerdict Containment { get; init; } =
        ChangeScopeContainmentVerdict.NotEvaluated;
}

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

/// <summary>
/// Whether a change's declared scope COVERS the resource it is asking to modify — the intra-lane
/// question, as distinct from <see cref="ChangeScopeCollisionVerdict"/>'s inter-lane one.
///
/// <para><b>Two questions, and only one of them had a name.</b> A collision verdict answers "do
/// these two lanes overlap"; it says nothing about whether a single lane's own declaration entitles
/// it to touch a given resource. A lane can be perfectly disjoint from every other lane and still be
/// asking to write something it never declared. Forge's legacy reservation layer answers that second
/// question with its own two-value decision — <c>CONTINUE</c> when the path is inside the reserved
/// scope, <c>SCOPE_CHANGE_REQUIRED</c> when it is not — and this enum is that vocabulary, carried
/// across the boundary rather than re-invented.</para>
/// </summary>
public enum ChangeScopeContainmentVerdict
{
    /// <summary>
    /// The containment question was never put, because the operation was refused for an earlier
    /// reason — a released reservation, an expired lease, a malformed record. Distinct from
    /// <see cref="WithinDeclaredScope"/> so that an unevaluated scope is never read as an approved
    /// one.
    /// </summary>
    NotEvaluated = 0,

    /// <summary>The declared scope covers this resource for write. The legacy <c>CONTINUE</c>.</summary>
    WithinDeclaredScope = 1,

    /// <summary>
    /// The declared scope does not cover this resource, so the change may not proceed until the
    /// scope is amended. The legacy <c>SCOPE_CHANGE_REQUIRED</c>. The specific cause — no
    /// declaration, no control-store item, a read-only item, or a different store named — is stated
    /// in the accompanying refusal reason.
    /// </summary>
    ScopeAmendmentRequired = 2,
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
