using System.Text.Json.Serialization;

namespace Nexus.ProductCore.Contracts.ReadModel;

/// <summary>
/// W10.6 — <b>the published Platform runtime read model.</b> Current observation of what Platform is
/// running, for a consumer that must never enumerate a host's processes itself.
///
/// <para>
/// <b>Why this is not a health endpoint and not a status field.</b> The estate has a demonstrated
/// failure mode for a single platform-wide lamp: it is either green and meaningless or red and
/// unattributable. This contract carries one observation per governed runtime unit, each with four
/// separate axes, and it carries the APPLICABILITY of every component — including the ones for which
/// runtime state is not a meaningful question at all.
/// </para>
///
/// <para>
/// <b>What this contract cannot say.</b> It cannot say a component that is not running has failed, and
/// it cannot say a component that IS running is healthy. Process state and health state are separate
/// members because a process can be up while the thing it serves is broken, and because a running
/// process whose health nobody probes yields <c>HealthState = Unknown</c> — which is a complete and
/// truthful answer, not a gap to be filled in.
/// </para>
/// </summary>
public sealed record PlatformRuntimeReadModel(
    string SchemaVersion,
    PlatformRuntimeReadSource Source,
    PlatformRuntimeReadPayload Payload);

/// <summary>Who observed this, from what, and when. <b>Not identity.</b></summary>
public sealed record PlatformRuntimeReadSource(
    string ContractVersion,
    string Authority,
    string SourceId,
    string SourceRevision,
    string ObservedAt,
    string PublishedAt,
    string PayloadDigest);

/// <summary>Everything the observation asserts, and nothing else.</summary>
public sealed record PlatformRuntimeReadPayload(
    PlatformRuntimeReadObserver Observer,
    IReadOnlyList<PlatformRuntimeReadUnit> Units,
    IReadOnlyList<PlatformRuntimeReadGap> Gaps);

/// <summary>
/// How the observation was made. Carried because a consumer must be able to tell an observation made
/// by a declared observer from one that merely appeared.
/// </summary>
public sealed record PlatformRuntimeReadObserver(
    string ObserverId,
    string ObserverKind,
    string HostIdentity,
    int GovernedComponentCount,
    DeliveryAuthorityClass Authority);

/// <summary>
/// <b>One governed runtime unit, observed.</b>
///
/// <para>
/// <b>A unit is a governed component, not a process.</b> <see cref="RuntimeUnitId"/> is derived from
/// the component's identity in the repository, is stable across observations, and is never a PID, a
/// timestamp, a host name or a port. Those four are precisely what a runtime observer is tempted to
/// key on, and each of them changes while the thing being identified does not.
/// </para>
///
/// <para>
/// <b>RL-11 binds here.</b> <c>ReservationLease.cs:25-28</c> in this same product states the rule the
/// hard way: <i>"PID is NEVER a liveness test. <c>Process.GetProcessById</c> on a recycled PID is a
/// false positive; on another host it is a false negative."</i> Nothing in this contract or the
/// observer that fills it inspects a process table, and no member here could carry a PID without
/// deleting another member first.
/// </para>
///
/// <para>
/// <b>THERE IS DELIBERATELY NO PER-UNIT TIMESTAMP, and a test found out why.</b> The first version of
/// this record carried <c>ObservedAt</c> on every unit. That put a clock inside the payload, and the
/// payload is what the semantic digest covers — so re-observing an unchanged estate produced a DIFFERENT
/// digest, and the digest could no longer answer the only question it exists to answer: "did anything
/// change?". The instant lives on <see cref="PlatformRuntimeReadSource.ObservedAt"/>, outside the
/// payload, exactly as it does for the other two authorities.
/// </para>
///
/// <para>
/// The consequence is stated rather than hidden: <b>freshness is a property of the PUBLICATION, not of
/// a unit</b>, because a snapshot observes every unit at one instant. Per-unit stale-ness would only
/// mean something if units could be observed at different times, and they cannot. Consumers read the
/// publication instant; see <c>PlatformRuntimeObservationState</c> for what a unit's state does say.
/// </para>
/// </summary>
public sealed record PlatformRuntimeReadUnit(
    /// <summary>Stable identity of the governed runtime unit. <b>Never a PID, never a timestamp.</b></summary>
    string RuntimeUnitId,
    /// <summary>The governed component this unit belongs to, named as the repository names it.</summary>
    string ComponentId,
    /// <summary>The component's path RELATIVE to the repository root. Never an absolute local path.</summary>
    string ComponentPath,
    /// <summary>What kind of thing this is. See <see cref="PlatformRuntimeApplicability"/>.</summary>
    string RuntimeApplicability,
    /// <summary>Whether the observation of this unit is current. See <see cref="PlatformRuntimeObservationState"/>.</summary>
    string ObservationState,
    /// <summary>Whether a process is running. See <see cref="PlatformRuntimeProcessState"/>.</summary>
    string ProcessState,
    /// <summary>Whether a health signal supports a claim. See <see cref="PlatformRuntimeHealthState"/>.</summary>
    string HealthState,
    /// <summary>What licensed the health value, if anything. See <see cref="PlatformRuntimeHealthAuthority"/>.</summary>
    string HealthAuthority,
    /// <summary>A reason CODE on failure, never prose and never a message that could carry a value.</summary>
    string ReasonCode,
    DeliveryAuthorityClass Authority);

/// <summary>
/// <b>An authoritative fact that is absent, stated rather than repaired.</b>
///
/// <para>
/// Shaped like the DevelopmentControl contract's gap record on purpose — the same question asked of a
/// third authority, and a second spelling of it would be a second thing to keep in step.
/// </para>
/// </summary>
public sealed record PlatformRuntimeReadGap(
    string Subject,
    string Kind,
    string Detail);

/// <summary>
/// <b>What KIND of thing a component is.</b>
///
/// <para>
/// TASK 3 proposed <c>Runnable | LibraryOnly | External | Unknown</c>. This is that set with
/// <c>Runnable</c> SPLIT, and the split is load-bearing rather than pedantic: a persistent service and
/// a one-shot command tool are both "runnable", and every downstream question — is it running now, is
/// it supposed to be, does "stopped" mean anything — has a different answer for each. Collapsing them
/// here would make the <c>ProcessState</c> axis unable to recover the distinction, which is exactly
/// the kind of lossy-first-axis design this contract exists to prevent.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlatformRuntimeApplicability>))]
public enum PlatformRuntimeApplicability
{
    /// <summary>No authority classified this component. Not a default — a reported absence.</summary>
    Unknown = 0,

    /// <summary>A persistent process that is expected to be running. <b>Measured: zero in Platform.</b></summary>
    Service = 1,

    /// <summary>
    /// An executable that runs, does a thing and exits. It has no standing process, so "running now"
    /// is a question about an event rather than a state.
    /// </summary>
    Command = 2,

    /// <summary>
    /// No entry point and no process, ever. <b>A library has no process health.</b> Reporting one as
    /// stopped, missing or unhealthy states a fault that cannot exist.
    /// </summary>
    LibraryOnly = 3,

    /// <summary>An assembly that runs only under a test runner. Not a deployed runtime.</summary>
    TestHost = 4,

    /// <summary>
    /// A runtime this repository does not own and holds no standing observer for. Recorded so that
    /// "not observed here" is never read as "not running".
    /// </summary>
    External = 5,
}

/// <summary>
/// <b>Whether a measurement of this unit exists, and whether it is current.</b>
///
/// <para>
/// <c>Missing</c> and <c>Unavailable</c> are different answers and both are needed. <c>Missing</c> is
/// "no observation has ever been recorded for this unit"; <c>Unavailable</c> is "an observation was
/// attempted and could not be completed". The first is a statement about the record; the second is a
/// statement about a failed attempt. Collapsing them would lose the only signal that distinguishes an
/// observer that is not running from one that is running and failing.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlatformRuntimeObservationState>))]
public enum PlatformRuntimeObservationState
{
    Unknown = 0,

    /// <summary>Measured, and within the freshness window.</summary>
    Current = 1,

    /// <summary>
    /// Measured, but older than the freshness window. <b>A stale reading is still a reading</b> — it
    /// is reported as stale rather than discarded, because "last observed healthy, 3 days ago" is
    /// more useful and more honest than "unknown".
    /// </summary>
    Stale = 2,

    /// <summary>No observation has been recorded for this unit.</summary>
    Missing = 3,

    /// <summary>An observation was attempted and could not be completed. <b>Not a zero.</b></summary>
    Unavailable = 4,

    /// <summary>Runtime state is not a meaningful question for this unit.</summary>
    NotApplicable = 5,
}

/// <summary>
/// <b>Whether a process is running.</b>
///
/// <para>
/// <c>NotApplicable</c> is the correct value for a library and for a one-shot command tool, and it is
/// not a euphemism for stopped: it says the question does not apply. <c>Unknown</c> says the question
/// applies and was not answered — which is what a failed observation yields, and it must never be
/// silently rendered as <c>Stopped</c>.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlatformRuntimeProcessState>))]
public enum PlatformRuntimeProcessState
{
    Unknown = 0,
    Running = 1,
    Stopped = 2,
    NotApplicable = 3,
}

/// <summary>
/// <b>Whether a health signal supports a claim about this unit.</b>
///
/// <para>
/// Independent of <see cref="PlatformRuntimeProcessState"/> by construction. A running process whose
/// health nobody probes is <c>Running</c> + <c>Unknown</c> — a complete answer. The one derivation
/// this contract forbids is inferring a health value FROM a process value: being up is not evidence of
/// being well, and the estate's own <c>Delivery</c> contract carries the same rule for certification.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlatformRuntimeHealthState>))]
public enum PlatformRuntimeHealthState
{
    /// <summary>Not measured. <b>Never green</b> — the rule <c>ControlHealth.cs:26-39</c> already states.</summary>
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Unhealthy = 3,
    NotApplicable = 4,
}

/// <summary>
/// <b>What licensed the health value.</b>
///
/// <para>
/// Ordered strongest first, and the order is the point: a health claim is only as good as its source,
/// and a consumer that cannot see the source cannot weigh the claim. <see cref="ProcessOnly"/> is
/// deliberately present and deliberately weak — it records that a process answered, and nothing about
/// whether what it serves works.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlatformRuntimeHealthAuthority>))]
public enum PlatformRuntimeHealthAuthority
{
    /// <summary>Nothing licensed a health value. Pairs with <c>HealthState = Unknown</c>.</summary>
    None = 0,

    /// <summary>A canonical readiness/liveness endpoint answered.</summary>
    HealthEndpoint = 1,

    /// <summary>A canonical service-specific health contract licensed it, with no endpoint probed.</summary>
    HealthContract = 2,

    /// <summary>
    /// Process observation alone. <b>The weakest authority there is</b>: it proves a process exists or
    /// does not, and says nothing about what it serves.
    /// </summary>
    ProcessOnly = 3,

    /// <summary>The unit declares no health signal and none is expected — a library, or a command.</summary>
    NotApplicable = 4,
}

/// <summary>The contract's identity, in one place so a producer and a consumer cannot disagree.</summary>
public static class PlatformRuntimeReadContract
{
    /// <summary>The versioned contract identifier.</summary>
    public const string SchemaVersion = "nexus.platform-runtime-read-model.v1";

    /// <summary>Who owns the facts. <b>Platform observes; Atlas consumes.</b></summary>
    public const string Authority = "Platform";

    /// <summary>The published file name.</summary>
    public const string FileName = "platform-runtime-read-model.v1.json";
}

/// <summary>
/// The gap kinds W10.6 carries, named once so a producer and a consumer cannot disagree.
/// </summary>
public static class PlatformRuntimeReadGapKinds
{
    /// <summary>
    /// Platform ships libraries and command tools; <b>it contains no persistent runtime service</b>.
    /// A consumer asking "is Platform up?" must be told that no such single thing exists, rather than
    /// shown an aggregate over components for which the question is meaningless.
    /// </summary>
    public const string NoPersistentRuntimeUnit = "PLATFORM_HAS_NO_PERSISTENT_RUNTIME_UNIT";

    /// <summary>
    /// No health endpoint exists anywhere in Platform, so no health value here can be stronger than
    /// <see cref="PlatformRuntimeHealthAuthority.ProcessOnly"/>. Recorded rather than left to be
    /// inferred from a column of <c>Unknown</c>.
    /// </summary>
    public const string NoHealthEndpoint = "PLATFORM_HEALTH_ENDPOINT_ABSENT";

    /// <summary>
    /// The deployed services that do run are not owned by this repository, and Platform holds no
    /// standing observer for them. <b>Not observed here is not the same as not running.</b>
    /// </summary>
    public const string ExternalRuntimeNotObserved = "EXTERNAL_RUNTIME_NOT_OBSERVED_BY_PLATFORM";

    /// <summary>The subject used by this contract's payload-level gaps.</summary>
    public const string PlatformSubject = "Platform";
}
