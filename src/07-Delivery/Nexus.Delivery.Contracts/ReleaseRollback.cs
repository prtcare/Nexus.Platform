using System.Text;
using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>What a release would fall back to if it had to be undone.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RollbackReferenceState
{
    /// <summary>
    /// <b>No previously accepted release exists.</b> This is the state of the first release into an
    /// environment, and it is a fact rather than a gap: there is nothing to fall back to, so the reachable
    /// remedy is a forward fix. Recorded explicitly so that "we did not look" and "there is nothing" cannot
    /// be confused — the same distinction the migration assessment draws for the same reason.
    /// </summary>
    NoPreviousAcceptedRelease,

    /// <summary>A previously accepted release exists and is named by <see cref="RollbackMetadata.PreviousReleaseId"/>.</summary>
    PreviousAcceptedReleaseExists
}

/// <summary>
/// The rollback information a later stage acts on.
///
/// <para>
/// <b>A fabricated previous release is the failure this type exists to prevent.</b> The tempting shortcut is
/// to point a first release at <c>something</c> so that its rollback metadata is not empty; that produces a
/// release whose rollback plan names a bundle that was never deployed, which is worse than no plan at all.
/// So the constructor refuses a previous release id in the <see cref="RollbackReferenceState.NoPreviousAcceptedRelease"/>
/// state, and refuses its absence in the other. The two members cannot disagree.
/// </para>
///
/// <para>
/// <b>Why the previous release is a <see cref="ReleaseId"/> and not a <see cref="BundleId"/>.</b> A bundle
/// is a set of artifacts; a release is what was promoted through an environment. Rolling an environment
/// back returns it to a release, and naming a bundle there would lose the identity the promotion proof was
/// built on.
/// </para>
/// </summary>
public sealed record RollbackMetadata
{
    private RollbackMetadata(
        RollbackReferenceState state,
        ReleaseId? previousReleaseId,
        bool crossesMigrationBoundary,
        bool rehearsed,
        string basis)
    {
        State = state;
        PreviousReleaseId = previousReleaseId;
        CrossesMigrationBoundary = crossesMigrationBoundary;
        Rehearsed = rehearsed;
        Basis = basis;
    }

    public RollbackReferenceState State { get; }

    /// <summary>Non-null exactly when <see cref="State"/> is <see cref="RollbackReferenceState.PreviousAcceptedReleaseExists"/>.</summary>
    public ReleaseId? PreviousReleaseId { get; }

    /// <summary>
    /// Whether undoing this release would cross a schema boundary. Carried because it is the single fact
    /// that separates an operation from an incident: rolling an artifact back across a migration boundary
    /// runs the new schema with the old code.
    /// </summary>
    public bool CrossesMigrationBoundary { get; }

    /// <summary>
    /// True only when the rollback path has actually been exercised. An unrehearsed rollback plan is a
    /// document, not a capability (rule R-7.1), so this is a member rather than a process note.
    /// </summary>
    public bool Rehearsed { get; }

    /// <summary>
    /// Why the rollback information says what it says. Stored rather than folded into
    /// <see cref="Summary"/>, because it is the part that has to survive a round trip through a registry:
    /// the summary is a rendering, and a rendering that had to be re-parsed to recover its reason would be
    /// a second implementation of the same judgement.
    /// </summary>
    public string Basis { get; }

    /// <summary>
    /// Operator-facing. Derived from <see cref="State"/>, <see cref="PreviousReleaseId"/> and
    /// <see cref="Basis"/>, so it cannot disagree with them. Must never contain a secret value.
    /// </summary>
    public string Summary => State == RollbackReferenceState.NoPreviousAcceptedRelease
        ? $"NO_PREVIOUS_ACCEPTED_RELEASE. {Basis}"
        : $"Roll back to {PreviousReleaseId}. {Basis}";

    /// <summary>
    /// A first release into an environment. There is nothing to roll back to, and that is recorded as the
    /// state rather than left as an absence.
    /// </summary>
    public static RollbackMetadata NoPreviousAcceptedRelease(bool crossesMigrationBoundary, string basis)
    {
        if (string.IsNullOrWhiteSpace(basis))
        {
            throw new ArgumentException(
                "Recording NO_PREVIOUS_ACCEPTED_RELEASE requires the basis it was established from. A first release and an unexamined history look identical without it.",
                nameof(basis));
        }

        return new RollbackMetadata(
            RollbackReferenceState.NoPreviousAcceptedRelease,
            previousReleaseId: null,
            crossesMigrationBoundary,
            rehearsed: false,
            basis);
    }

    /// <summary>A later release, pointing at the release the environment would return to.</summary>
    public static RollbackMetadata ToPreviousRelease(
        ReleaseId previousReleaseId,
        bool crossesMigrationBoundary,
        bool rehearsed,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(previousReleaseId);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rollback reference must carry the reason it points where it does.", nameof(reason));
        }

        return new RollbackMetadata(
            RollbackReferenceState.PreviousAcceptedReleaseExists,
            previousReleaseId,
            crossesMigrationBoundary,
            rehearsed,
            reason);
    }

    /// <summary>
    /// The W9.1 rollback request this metadata would produce, so the later stage consumes one mechanism
    /// rather than two. Returns null when there is no previous release to plan for.
    /// </summary>
    public RollbackRequest? ToRequest(
        BundleId currentBundleId,
        BundleId? previousBundleId,
        DeploymentEnvironmentId environment,
        bool previousBundleAvailable,
        DeploymentAuthorization? authorization = null)
    {
        if (State == RollbackReferenceState.NoPreviousAcceptedRelease)
        {
            return null;
        }

        return new RollbackRequest(
            currentBundleId,
            previousBundleId,
            environment,
            previousBundleAvailable,
            CrossesMigrationBoundary,
            Rehearsed,
            authorization,
            Summary);
    }

    internal void AppendCanonicalForm(StringBuilder builder)
        => builder.Append(State).Append('\0')
                  .Append(PreviousReleaseId?.Value ?? string.Empty).Append('\0')
                  .Append(CrossesMigrationBoundary).Append('\0')
                  .Append(Rehearsed).Append('\0')
                  .Append(Basis).Append('\0');

    public override string ToString() => State == RollbackReferenceState.NoPreviousAcceptedRelease
        ? "NO_PREVIOUS_ACCEPTED_RELEASE"
        : $"rollback → {PreviousReleaseId}";
}
