using System.Text.Json.Serialization;

namespace Nexus.ProductCore.Contracts.ReadModel;

/// <summary>
/// W10.4 — <b>the published DevelopmentControl read model.</b> A projection of DevelopmentControl
/// authority, for a consumer that must never read the workbook.
///
/// <para>
/// <b>Why a projection and not the workbook.</b> The workbook is the authority. A consumer that opened
/// it would need the locking protocol, the schema, the sheet layout and the write rules — and would
/// then be one refactor away from writing to it. The owner publishes; the consumer reads what was
/// published. That is the same boundary W10.1 established for Delivery, applied to the second owner.
/// </para>
///
/// <para>
/// <b>DevelopmentControl remains the sole authority.</b> This projection is produced by the owner, from
/// the owner's own reader, and nothing here can change the workbook. If it is deleted the authority is
/// unchanged; if it disagrees, the authority wins.
/// </para>
///
/// <para>
/// <b>The classification is carried, not collapsed.</b> A completed work item is a record in a
/// workbook. It is <b>not</b> an observation that a process is running now, and a WorkItem whose
/// lineage reaches a Release does not make that Release healthy. See
/// <see cref="DevelopmentControlAuthorityClass"/>.
/// </para>
/// </summary>
public sealed record DevelopmentControlReadModel(
    string SchemaVersion,
    DevelopmentControlReadSource Source,
    DevelopmentControlReadPayload Payload);

/// <summary>
/// Who published this, from what, and when. <b>Not identity.</b>
///
/// <para>
/// <see cref="PayloadDigest"/> is the semantic identity: computed over the payload alone, so
/// republishing unchanged authority state produces the same digest while the instants move. No
/// timestamp may enter the digest.
/// </para>
/// </summary>
public sealed record DevelopmentControlReadSource(
    string ContractVersion,
    string Authority,
    string SourceId,
    string SourceRevision,
    string ObservedAt,
    string PublishedAt,
    string PayloadDigest);

/// <summary>Everything the projection asserts, and nothing else.</summary>
public sealed record DevelopmentControlReadPayload(
    DevelopmentControlReadControl Control,
    IReadOnlyList<DevelopmentControlReadWorkItem> WorkItems,
    IReadOnlyList<DevelopmentControlReadChangeScope> ChangeScopes,
    IReadOnlyList<DevelopmentControlReadChangeRequest> ChangeRequests,
    IReadOnlyList<DevelopmentControlReadDependency> Dependencies,
    IReadOnlyList<DevelopmentControlReadLineageEdge> LineageEdges,
    DevelopmentControlReadGovernanceRegistry Governance,
    IReadOnlyList<DevelopmentControlReadGap> Gaps);

/// <summary>
/// The control's own state, read from the authority's declared sites.
///
/// <para>
/// This is the <b>authority marker</b>, not a health figure. It answers "does this workbook govern"
/// and nothing about whether anything is running.
/// </para>
/// </summary>
public sealed record DevelopmentControlReadControl(
    string State,
    string SchemaId,
    string SchemaVersion,
    int SheetCount,
    DeliveryAuthorityClass DevelopmentControlAuthorityClass,
    string WrittenAt);

/// <summary>
/// One work item, as the authority records it.
///
/// <para>
/// <b>Every field name here was read from the workbook's own binding table, not from the domain
/// documentation.</b> The physical sheet is <c>07_WorkItems</c> but its LOGICAL name is
/// <c>WorkGraph</c>, and the columns are <c>WorkId</c>, <c>Title</c>, <c>Status</c>, <c>HeadId</c>,
/// <c>Owner</c>, <c>ReadinessState</c>, <c>AcceptanceCriteria</c>, <c>EvidenceRequired</c>,
/// <c>DependsOn</c>, <c>ScopeId</c> — measured, because a projection written against the names one
/// would expect reads every column as empty and reports a populated workbook as an empty one.
/// </para>
/// </summary>
public sealed record DevelopmentControlReadWorkItem(
    string WorkItemId,
    string Title,
    string Status,
    string Owner,
    string HeadId,
    string LayerId,
    string ReadinessState,
    string AcceptanceCriteria,
    string EvidenceRequired,
    string WorkType,
    string Priority,
    string ParentWorkItemId,
    IReadOnlyList<string> DependsOn,
    DeliveryAuthorityClass Authority);

/// <summary>
/// One change scope — the declaration of what a work item is allowed to touch.
///
/// <para>
/// <b>Deliberately not a work item field.</b> A scope is its own record with its own identity, because
/// two work items may declare the same scope and a scope may outlive the item that declared it.
/// </para>
/// </summary>
public sealed record DevelopmentControlReadChangeScope(
    string ChangeScopeId,
    string WorkItemId,
    string ScopeClass,
    string Target,
    string Access,
    string ReviewState,
    DeliveryAuthorityClass Authority);

/// <summary>One Core Change Request — a request aimed at a core/platform owner.</summary>
public sealed record DevelopmentControlReadChangeRequest(
    string RequestId,
    string RequestType,
    string SourceWorkItemId,
    string TargetOwner,
    string CapabilityRequested,
    string Status,
    DeliveryAuthorityClass Authority);

/// <summary>A dependency edge between two work items.</summary>
public sealed record DevelopmentControlReadDependency(
    string FromWorkItemId,
    string ToWorkItemId,
    string RelationType,
    string Status,
    DeliveryAuthorityClass Authority);

/// <summary>
/// One lineage edge. <b>Only edges the authority holds are published.</b>
///
/// <para>
/// The chain this exists to carry is WorkItem → ChangeScope → Repository → SourceCommit, plus the
/// <c>SourceCommit</c> values Delivery also records — which is the one identifier two authorities
/// publish independently and can therefore be joined on. See
/// <c>LINEAGE_JOIN_CONTRACT.md</c>.
/// </para>
/// </summary>
public sealed record DevelopmentControlReadLineageEdge(
    string From,
    string To,
    string Kind,
    DeliveryAuthorityClass Authority);

/// <summary>
/// <b>An authoritative edge that is absent, stated rather than repaired.</b>
///
/// <para>
/// A consumer that finds a gap knows the lineage is incomplete. A consumer handed a plausible
/// reconstruction would not — and would act on it.
/// </para>
/// </summary>
public sealed record DevelopmentControlReadGap(
    string Subject,
    string Kind,
    string Detail);

/// <summary>
/// W10.5B — <b>the Governance Gate Registry, with its own availability carried beside it.</b>
///
/// <para>
/// <b>A registry that could not be read is not a registry with no gates.</b> That sentence is the
/// whole reason this is a record and not a bare collection. A consumer handed
/// <c>GovernanceGates: []</c> cannot tell "this control governs nothing" from "the governance sheet
/// was not in the workbook" or "its identity column had moved", and only one of those is safe to
/// render as a zero. <see cref="State"/> is that distinction, stated in the document rather than
/// inferred from a field's absence — the same shape Delivery uses for
/// <c>Backup.Authority = Unavailable</c>, whose own detail line reads "an absent source, not a
/// failure, and not a reported zero".
/// </para>
///
/// <para>
/// <b>An empty <see cref="Gates"/> list is only meaningful when <see cref="State"/> is
/// <see cref="Available"/>.</b> The publisher enforces that pairing rather than trusting it: a
/// document claiming AVAILABLE with no gates is a legitimate zero, and a document claiming
/// UNAVAILABLE asserts nothing about gates at all.
/// </para>
/// </summary>
public sealed record DevelopmentControlReadGovernanceRegistry(
    string State,
    string Detail,
    IReadOnlyList<DevelopmentControlReadGovernanceGate> Gates)
{
    /// <summary>The registry was read from the authority. Its gate list, empty or not, is a reading.</summary>
    public const string Available = "AVAILABLE";

    /// <summary>The registry could not be read. <b>Nothing</b> is being asserted about gates.</summary>
    public const string Unavailable = "UNAVAILABLE";
}

/// <summary>
/// W10.5B — <b>one Row of the Governance Gate Registry, as the authority records it.</b>
///
/// <para>
/// <b>This is a REGISTRY ENTRY, not a governance decision.</b> The distinction is the finding that
/// shaped this milestone, and it is structural here rather than documented: there is no member named
/// <c>Verdict</c>, no <c>Decision</c>, no <c>Approved</c>, <c>Refused</c>, <c>Deferred</c>,
/// <c>Passed</c> or <c>Failed</c>, and there is no way to add one to this record without deleting a
/// member first. A view built over this type therefore cannot claim an approval the authority never
/// recorded — not because it is told not to, but because the fact is not in the document.
/// </para>
///
/// <para>
/// <b>Why <c>RegistryStatus</c> and not <c>Status</c>.</b> The workbook's header is <c>Status</c> and
/// stays <c>Status</c> — the authority is not renamed. What this contract refuses is exposing it
/// under a name that invites a decision reading. Measured, the column carries PLANNING states:
/// <c>Proposed</c>, <c>Not Started</c>, <c>Planned</c>, <c>Superseded</c> and ten further values,
/// and it carries no <c>APPROVED</c>, no <c>REFUSED</c> and no <c>DEFERRED_BY_OWNER</c> at all.
/// The values are published RAW and un-normalized: mapping fourteen measured states onto an invented
/// canonical vocabulary would replace what the authority says with what this contract wished it said.
/// </para>
///
/// <para>
/// <b>Every field name was measured on row 4 of <c>18_Governance</c>, re-measured in this milestone
/// rather than transcribed</b>, including the ones that turned out to be constants. Three are worth
/// naming because a consumer will otherwise misread them: <see cref="AuthorityProfile"/> is the
/// single value <c>PLATFORM_AUTHORITY</c> on every row, so it identifies the owning profile and
/// <b>not</b> a per-record authority; <see cref="IsCurrent"/> is <c>Yes</c> on every row, so no
/// supersession exists and <see cref="SupersedesVersion"/> has nothing to describe.
/// </para>
///
/// <para>
/// <b>The three join-candidate fields are published precisely because they are blank.</b>
/// <see cref="GateId"/>, <see cref="EnvelopeChangeId"/> and <see cref="SupersedesVersion"/> are the
/// only members that could carry an identifier another contract also publishes, and all three are
/// empty on every measured row. Publishing them is what lets a consumer <b>check</b>
/// <c>GOVERNANCE_CROSS_SOURCE_JOIN_GAP</c> instead of taking it on trust — a gap asserted in prose
/// is a claim, and a gap a reader can re-measure from the document is evidence.
/// </para>
/// </summary>
public sealed record DevelopmentControlReadGovernanceGate(
    string GovernanceId,
    string GateId,
    string Name,
    string AuthorityProfile,
    string RequiredEvidence,
    string RegistryStatus,
    string BlocksScope,
    string Notes,
    string IsCurrent,
    string EffectiveFrom,
    string EnvelopeChangeId,
    string SupersedesVersion,
    DevelopmentControlReadGovernanceSource Source,
    DeliveryAuthorityClass Authority);

/// <summary>
/// The migration envelope as this registry carries it. <b>Provenance, not a gate fact.</b>
///
/// <para>
/// Kept as its own record rather than flattened into the gate so that a consumer cannot mistake a
/// migration column for something the governance authority decided. These nine columns appear with
/// these header names on every V3 sheet; here they record which legacy workbook, sheet and record
/// each governance entry was migrated from.
/// </para>
///
/// <para>
/// <see cref="SourceRevision"/> and <see cref="SourceArchitectureVersion"/> read <c>UNKNOWN</c> on
/// every row — the authority's own declared provenance for those two is "unknown", which is a
/// recorded value and not a missing one. <see cref="SourceRecordId"/> is populated on every row and
/// is the row's identity <b>in the sheet it came from</b>; it is not a key in this registry and it
/// does not join to anything the contract publishes.
/// </para>
/// </summary>
public sealed record DevelopmentControlReadGovernanceSource(
    string SourceForm,
    string SourceWorkbook,
    string SourceWorkbookHash,
    string SourceSheet,
    string SourceRecordId,
    string SourceRevision,
    string SourceArchitectureVersion,
    string MigrationTimestamp,
    string MigrationTransformation);

/// <summary>
/// <b>The gap kinds W10.5B carries, named once so a producer and a consumer cannot disagree.</b>
///
/// <para>
/// Both are EXPECTED and non-blocking: they are gaps in what the authority records, not defects in
/// this projection. Both must render as explicit absences. Neither may be rendered as a zero, an
/// empty list, or a blank cell — a consumer shown "0" has been told the opposite of the truth.
/// </para>
/// </summary>
public static class DevelopmentControlReadGapKinds
{
    /// <summary>
    /// Governance computes a verdict at runtime and discards it. There is no durable store of
    /// evaluation history, so <b>no historical governance decision exists to read</b>. A view that
    /// rendered its own runtime evaluation as history would be presenting a computed result as a
    /// recorded fact.
    /// </summary>
    public const string GovernanceEvaluationHistorySourceGap = "GOVERNANCE_EVALUATION_HISTORY_SOURCE_GAP";

    /// <summary>
    /// No stable identifier in the Gate Registry is shared with any other contract:
    /// <c>GateId</c>, <c>ChangeId</c> and <c>SupersedesVersion</c> are blank on every row, and
    /// <c>BlocksScope</c> is free text on the minority of rows that carry it. Governance therefore
    /// <b>cannot</b> be joined to a work item, change scope, release or deployment. Names, prose,
    /// timestamps and row position are all explicitly not substitutes.
    /// </summary>
    public const string GovernanceCrossSourceJoinGap = "GOVERNANCE_CROSS_SOURCE_JOIN_GAP";

    /// <summary>The sheet the registry is read from, named once for gap subjects and for consumers.</summary>
    public const string GovernanceRegistrySubject = "GovernanceGates";
}

/// <summary>
/// How a DevelopmentControl fact came to be known.
///
/// <para>
/// Reused from the Delivery contract deliberately — the vocabulary is the same question asked of a
/// second authority, and a second spelling of it would be a second thing to keep in step.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeliveryAuthorityClass>))]
public enum DeliveryAuthorityClass
{
    /// <summary>The DevelopmentControl authority asserts this. Every workbook row is this.</summary>
    Authoritative,

    /// <summary>Computed by this projection from authoritative facts. Counts, derived sets.</summary>
    Derived,

    /// <summary>Observed from a running system. <b>Nothing here is this class</b> — a workbook does not
    /// observe a process, and no runtime source exists.</summary>
    ObservedRuntime,

    /// <summary>Recorded once, at a time. Completed work, historical revisions.</summary>
    HistoricalEvidence,

    /// <summary>No authority answers this.</summary>
    Unavailable
}

/// <summary>The contract's identity, in one place so a producer and a consumer cannot disagree.</summary>
public static class DevelopmentControlReadContract
{
    /// <summary>The versioned contract identifier.</summary>
    public const string SchemaVersion = "nexus.development-control-read-model.v1";

    /// <summary>Who owns the facts.</summary>
    public const string Authority = "DevelopmentControl";

    /// <summary>The published file name.</summary>
    public const string FileName = "development-control-read-model.v1.json";
}
