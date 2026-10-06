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
