using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>One link in the release lineage chain, outermost first.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseLineageHopKind
{
    /// <summary>The governed work the release was performed for. The outermost link, and the only one no build records.</summary>
    GovernedWork,

    /// <summary>The source commit the bytes came from.</summary>
    SourceCommit,

    /// <summary>The certified build that produced them.</summary>
    Build,

    /// <summary>The addressable artifact in the store.</summary>
    Artifact,

    /// <summary>The release. The innermost link and the one a reader starts from.</summary>
    Release
}

/// <summary>One resolved link, with the identity it resolved to.</summary>
public sealed record ReleaseLineageHop(ReleaseLineageHopKind Kind, string Identity, string? Detail = null);

/// <summary>
/// A completed or failed walk of the lineage chain, from the release back to the governed work.
///
/// <para>
/// <b>The walk is a proof, not a data structure.</b> TASK 11 requires <c>ReleaseId → artifact → build →
/// source commit → originating governed work</c> to be navigable in reverse; a chain that merely stores
/// those fields proves nothing, because storing them is what a claim looks like before anyone checks it.
/// So the hops are produced by <b>resolving each link against the store that owns it</b> — the registry for
/// the release, the artifact store for the artifact, the manifest for the build and the commits. A link
/// that does not resolve stops the walk and is named, rather than being filled in from the record's own
/// recollection.
/// </para>
/// </summary>
public sealed record ReleaseLineageWalk
{
    private ReleaseLineageWalk(
        bool isComplete,
        IReadOnlyList<ReleaseLineageHop> hops,
        ReleaseLineageHopKind? stoppedAt,
        string detail)
    {
        IsComplete = isComplete;
        Hops = hops;
        StoppedAt = stoppedAt;
        Detail = detail;
    }

    /// <summary>True only when every link resolved. A partial walk is not a lineage.</summary>
    public bool IsComplete { get; }

    /// <summary>Resolved links, ordered from the governed work inwards to the release.</summary>
    public IReadOnlyList<ReleaseLineageHop> Hops { get; }

    /// <summary>The link the walk could not resolve. Null when complete.</summary>
    public ReleaseLineageHopKind? StoppedAt { get; }

    /// <summary>Operator-facing. Must never contain a secret value.</summary>
    public string Detail { get; }

    /// <summary>The rendered chain, e.g. <c>WI-09-3.1 → 2f6f930 → bld-… → marketsurvey.api@0.1.0 → rel-…</c>.</summary>
    public string Describe() => string.Join(" → ", Hops.Select(h => h.Identity));

    public static ReleaseLineageWalk Complete(IReadOnlyList<ReleaseLineageHop> hops)
        => new(true, hops, null, $"Lineage complete across {hops.Count} link(s).");

    public static ReleaseLineageWalk Broken(IReadOnlyList<ReleaseLineageHop> hops, ReleaseLineageHopKind stoppedAt, string detail)
        => new(false, hops, stoppedAt, detail);
}

/// <summary>
/// The release's position in the lineage chain, appended when the release is created.
///
/// <para>
/// <b>Append-only, and a second release is a second row.</b> The estate's rule for the deployment lineage
/// log applies here unchanged: never re-run a scenario that has written an immutable record, never
/// backfill, and prefer re-executing over patching when records must describe a run.
/// </para>
///
/// <para>
/// <b>Key names, never values.</b> Invariant L-1 is inherited from <see cref="DeploymentLineageRecord"/>:
/// this record carries no member that could hold a configuration value or a credential value.
/// </para>
/// </summary>
public sealed record ReleaseLineageRecord
{
    /// <summary>Continues the estate's <c>L-W9-*</c> series rather than starting a second ledger.</summary>
    public const string SeriesPrefix = "L-W9-";

    public ReleaseLineageRecord(
        string lineageId,
        DateTimeOffset occurredAt,
        ReleaseId releaseId,
        DeploymentUnitId unitId,
        string version,
        BuildId buildId,
        BundleId bundleId,
        string releaseRefName,
        IReadOnlyList<string> sourceCommits,
        IReadOnlyList<ReleaseArtifactIdentity> artifacts,
        GovernedWorkReference? originatingWork = null,
        string? buildManifestReference = null,
        string? reason = null)
    {
        if (!IsValidLineageId(lineageId))
        {
            throw new ArgumentException(
                $"A lineage id is '{SeriesPrefix}<digits>' (continuing the estate's L-series). Received '{lineageId}'.",
                nameof(lineageId));
        }

        ArgumentNullException.ThrowIfNull(releaseId);
        ArgumentNullException.ThrowIfNull(unitId);
        ArgumentNullException.ThrowIfNull(buildId);
        ArgumentNullException.ThrowIfNull(bundleId);
        ArgumentNullException.ThrowIfNull(artifacts);

        if (string.IsNullOrWhiteSpace(releaseRefName))
        {
            throw new ArgumentException("A release lineage record must name the release reference.", nameof(releaseRefName));
        }

        if (sourceCommits is null || sourceCommits.Count == 0)
        {
            throw new ArgumentException("A release lineage record must name its source commits.", nameof(sourceCommits));
        }

        if (artifacts.Count == 0)
        {
            throw new ArgumentException("A release lineage record must name the artifacts it releases.", nameof(artifacts));
        }

        foreach (var commit in sourceCommits)
        {
            if (string.IsNullOrWhiteSpace(commit) || !IsHex(commit))
            {
                throw new ArgumentException("A source commit in a lineage record must be hexadecimal.", nameof(sourceCommits));
            }
        }

        if (reason is not null && CredentialShape.LooksLikeCredentialValue(reason))
        {
            throw new ArgumentException("A free-text reason must not contain a credential-shaped value. Invariant L-1.", nameof(reason));
        }

        LineageId = lineageId;
        OccurredAt = occurredAt;
        ReleaseId = releaseId;
        UnitId = unitId;
        Version = version;
        BuildId = buildId;
        BundleId = bundleId;
        ReleaseRefName = releaseRefName;
        SourceCommits = [.. sourceCommits.Select(c => c.ToLowerInvariant()).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal)];
        Artifacts = [.. artifacts.OrderBy(a => a.ArtifactId.Value, StringComparer.Ordinal)];
        OriginatingWork = originatingWork;
        BuildManifestReference = buildManifestReference;
        Reason = reason;
    }

    public string LineageId { get; }

    public DateTimeOffset OccurredAt { get; }

    public ReleaseId ReleaseId { get; }

    public DeploymentUnitId UnitId { get; }

    public string Version { get; }

    public BuildId BuildId { get; }

    public BundleId BundleId { get; }

    public string ReleaseRefName { get; }

    public IReadOnlyList<string> SourceCommits { get; }

    public IReadOnlyList<ReleaseArtifactIdentity> Artifacts { get; }

    /// <summary>The outermost link. Null when the release was not performed for a recorded piece of governed work.</summary>
    public GovernedWorkReference? OriginatingWork { get; }

    public string? BuildManifestReference { get; }

    public string? Reason { get; }

    private static bool IsValidLineageId(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.StartsWith(SeriesPrefix, StringComparison.Ordinal)
           && value.Length > SeriesPrefix.Length
           && value[SeriesPrefix.Length..].All(char.IsAsciiDigit);

    private static bool IsHex(string value)
        => value.All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    public override string ToString()
        => $"{LineageId} {ReleaseId} ({UnitId}@{Version})";
}

/// <summary>
/// The append-only release lineage log.
///
/// <para>
/// <b>A second log, not a second ledger.</b> It continues the same <c>L-W9-*</c> series as
/// <see cref="IDeploymentLineageLog"/>; the two differ in what they record, not in when or by whom. A
/// deployment record describes a promotion act and is written by the promotion path; a release record
/// describes the release's position in the chain and is written once, when the release is created. W9.3
/// stops at the release: the DEV deployment id is W9.4's.
/// </para>
/// </summary>
public interface IReleaseLineageLog
{
    /// <summary>
    /// Appends a record. Refuses a duplicate <see cref="ReleaseLineageRecord.LineageId"/> and a second
    /// record for the same <see cref="ReleaseId"/> — a release occupies one position in the chain, and a
    /// second row claiming the same position is a correction attempt wearing an append's clothing.
    /// </summary>
    Task AppendAsync(ReleaseLineageRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReleaseLineageRecord>> ReadAsync(CancellationToken cancellationToken = default);

    Task<ReleaseLineageRecord?> FindByReleaseIdAsync(ReleaseId releaseId, CancellationToken cancellationToken = default);
}
