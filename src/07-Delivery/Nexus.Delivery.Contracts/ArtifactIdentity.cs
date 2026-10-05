using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// Whether an <see cref="ArtifactId"/> may still be written to.
///
/// <para>
/// <b>The two states exist so that "we no longer want this payload" and "this id was never used" are
/// different facts.</b> Before this type there was only the second: an artifact that had been withdrawn was
/// indistinguishable from one that had never existed, because the only record of it lived inside the
/// directory that was removed. The W9.5 incident is exactly that shape — an uncertified payload was deleted,
/// the id became free again, and a materially different payload was published under it.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArtifactIdentityState
{
    /// <summary>The id is bound to <see cref="ArtifactIdentity.ContentDigest"/> and may be re-published only with those same bytes.</summary>
    Active,

    /// <summary>
    /// The id was withdrawn by a governed act. <b>It is never reusable</b>, whatever happens to the payload.
    /// A withdrawn version is a version that is spent — the remedy is a new version, which is what makes the
    /// version number an honest statement about how many payloads have occupied this identity.
    /// </summary>
    Withdrawn
}

/// <summary>
/// <b>The identity binding: one <see cref="ArtifactId"/> to exactly one immutable payload digest.</b>
///
/// <para>
/// <b>Where it lives is the whole point.</b> Before W9.6 the only record binding an id to its bytes was
/// <c>entry.json</c>, stored INSIDE the artifact's own directory. Removing that directory therefore removed
/// the binding, and the store — which reads the directory to decide whether an id is taken — concluded the id
/// was free. That is not a hostile scenario; it is what a lane does when it wants to discard a failed build,
/// and it happened. The W9.5 remediation removed an uncertified payload from the governed store, and a
/// different payload subsequently occupied the same id. The bytes differed by 126 and the build id was
/// identical, because a build id digests inputs rather than output.
/// </para>
///
/// <para>
/// <b>So the identity moved out of the payload.</b> This record lives in the store's own
/// <c>identities/</c> directory, is written once when an id is first accepted, and is <b>not</b> removed when
/// the payload is. Publishing an id consults this record <i>before</i> the payload directory, so a deleted
/// payload leaves the id taken rather than free. The store still cannot defend against the destruction of its
/// own identity records — nothing can — but it no longer treats a missing payload as proof that an id was
/// never used.
/// </para>
///
/// <para>
/// <b>Not a second registry.</b> There is no second index, no second digest and no second reader: this record
/// holds the same <see cref="ArtifactDigest"/> the <c>entry.json</c> holds, written by the same publish call,
/// and <c>ResolveByArtifactIdAsync</c> continues to read the payload entry. The identity record exists to
/// answer one question the payload entry cannot answer once it is gone — <i>was this id ever used?</i> — and
/// it answers nothing else.
/// </para>
/// </summary>
public sealed record ArtifactIdentity
{
    public const string CurrentSchema = "nexus-artifact-identity-v1";

    public required string SchemaVersion { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    /// <summary>
    /// <b>The one digest this id has ever held.</b> Not the latest — the only. A second, different digest for
    /// the same id is refused at the store rather than recorded here, so this member can never need revising.
    /// </summary>
    public required ArtifactDigest ContentDigest { get; init; }

    public required BuildId BuildId { get; init; }

    public required DateTimeOffset FirstAcceptedUtc { get; init; }

    public required ArtifactIdentityState State { get; init; }

    /// <summary>How many publish attempts this id has seen, accepted and refused. A count, not a log — the reasons live in the attempts file.</summary>
    public required int PublishAttempts { get; init; }

    /// <summary>Why it was withdrawn. Required when <see cref="State"/> is <see cref="ArtifactIdentityState.Withdrawn"/>.</summary>
    public string? WithdrawnReason { get; init; }

    public DateTimeOffset? WithdrawnUtc { get; init; }

    /// <summary>Who withdrew it. A lane identity, like every other actor this estate records.</summary>
    public string? WithdrawnBy { get; init; }

    /// <summary>True when this id may not be written to again, for any reason.</summary>
    public bool IsWithdrawn => State == ArtifactIdentityState.Withdrawn;

    /// <summary>
    /// Whether <paramref name="candidate"/> is the payload this id holds. <b>The comparison is the whole
    /// control</b>: identical bytes are an idempotent re-publish, and anything else is a new build that must
    /// carry a new id.
    /// </summary>
    public bool Holds(ArtifactDigest candidate) => ContentDigest == candidate;
}

/// <summary>Why an identity-level publish refusal happened, as distinct from a payload-level one.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArtifactIdentityRefusalReason
{
    None = 0,

    /// <summary>The id was withdrawn by a governed act and may never be written to again.</summary>
    ArtifactIdWithdrawn,

    /// <summary>The id is bound to a different payload. Reported even when the payload directory is absent.</summary>
    ArtifactIdBoundToDifferentPayload,

    /// <summary>A withdrawal was requested with no reason, or on an id that does not exist.</summary>
    WithdrawalNotPermitted
}

/// <summary>
/// The outcome of the one governed destructive act this store supports.
///
/// <para>
/// <b>Why withdrawal rather than deletion.</b> The estate's doctrine is that an immutable record is corrected
/// by supersession, never by rewrite, and that a scenario which has written one is not re-run. Until W9.6 the
/// store had no expression for that: the only way to remove a payload was to delete its directory, which also
/// destroyed the evidence that the id had been used. Withdrawal gives the same intent a governed form that
/// leaves the identity taken and states why — so the next attempt is refused by mechanism rather than by
/// somebody remembering.
/// </para>
/// </summary>
/// <param name="IsWithdrawn">True when the identity is now <see cref="ArtifactIdentityState.Withdrawn"/>.</param>
/// <param name="PayloadRemoved">Whether the payload bytes were also removed. The identity survives either way.</param>
/// <param name="Reason">Why it was refused, when it was.</param>
public sealed record ArtifactWithdrawalOutcome(
    bool IsWithdrawn,
    bool PayloadRemoved,
    ArtifactIdentityRefusalReason Reason,
    string? Detail)
{
    public static ArtifactWithdrawalOutcome Withdrawn(bool payloadRemoved, string detail)
        => new(true, payloadRemoved, ArtifactIdentityRefusalReason.None, detail);

    public static ArtifactWithdrawalOutcome Refused(ArtifactIdentityRefusalReason reason, string detail)
        => new(false, false, reason, detail);
}
