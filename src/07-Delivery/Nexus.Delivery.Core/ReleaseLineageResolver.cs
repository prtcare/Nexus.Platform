using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// Walks the release lineage chain backwards, <b>resolving every link against the store that owns it</b>.
///
/// <para>
/// <c>ReleaseId → artifact → build → source commit → originating governed work</c> is the direction TASK 11
/// requires, and the direction is the point: a chain that can only be read forwards is a description
/// written by whoever created the release, while a chain that can be read backwards is a claim someone else
/// can check against the artefacts themselves.
/// </para>
///
/// <para>
/// <b>Each hop is resolved, not recited.</b> The release is read from the registry; the artifact must be
/// present in the artifact store; the build must resolve to the artifacts it produced; the source commits
/// are taken from the build manifest when one is supplied and otherwise from the release's own record, and
/// the walk reports which; the governed work comes from the lineage ledger. A link that does not resolve
/// stops the walk and is named — it is never filled in from the record's own recollection, because that is
/// the difference between a lineage and a restatement.
/// </para>
/// </summary>
public sealed class ReleaseLineageResolver
{
    private readonly IReleaseRegistry _registry;
    private readonly IArtifactStore _store;
    private readonly IReleaseLineageLog? _lineageLog;

    public ReleaseLineageResolver(
        IReleaseRegistry registry,
        IArtifactStore store,
        IReleaseLineageLog? lineageLog = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lineageLog = lineageLog;
    }

    /// <summary>
    /// Walks from a release id back to the governed work.
    /// </summary>
    /// <param name="releaseId">Where the walk starts.</param>
    /// <param name="manifest">
    /// The build manifest, when it is available. Supplying it lets the source-commit link be verified
    /// against the build's own record rather than believed from the release's summary of it; omitting it is
    /// permitted, and the walk says so in its detail rather than implying the stronger claim.
    /// </param>
    public async Task<ReleaseLineageWalk> WalkBackFromReleaseAsync(
        ReleaseId releaseId,
        BuildManifest? manifest = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(releaseId);

        var hops = new List<ReleaseLineageHop>();

        // ---- 1. The release ---------------------------------------------------------------------------
        var release = await _registry.TryOpenAsync(releaseId, cancellationToken).ConfigureAwait(false);

        if (release is null)
        {
            return ReleaseLineageWalk.Broken(
                hops,
                ReleaseLineageHopKind.Release,
                $"No release '{releaseId}' is registered, so the walk cannot start.");
        }

        hops.Add(new ReleaseLineageHop(
            ReleaseLineageHopKind.Release,
            releaseId.Value,
            $"{release.UnitId}@{release.Version}"));

        // ---- 2. The artifact --------------------------------------------------------------------------
        // Every artifact the release names must be resolvable from the store. Resolved from the STORE, so
        // the link is established against the bytes rather than against the release's description of them.
        foreach (var artifact in release.Artifacts)
        {
            var entry = await _store.ResolveByArtifactIdAsync(artifact.ArtifactId, cancellationToken).ConfigureAwait(false);

            if (entry is null)
            {
                return ReleaseLineageWalk.Broken(
                    hops,
                    ReleaseLineageHopKind.Artifact,
                    $"'{artifact.ArtifactId.Value}' is named by {releaseId} but is not in the artifact store.");
            }

            if (entry.ContentDigest != artifact.ContentDigest)
            {
                return ReleaseLineageWalk.Broken(
                    hops,
                    ReleaseLineageHopKind.Artifact,
                    $"'{artifact.ArtifactId.Value}' is stored as {entry.ContentDigest} but the release names "
                    + $"{artifact.ContentDigest}. The store's contents and the release disagree.");
            }

            hops.Add(new ReleaseLineageHop(
                ReleaseLineageHopKind.Artifact,
                artifact.ArtifactId.Value,
                artifact.ContentDigest.ToString()));
        }

        // ---- 3. The build -----------------------------------------------------------------------------
        var produced = await _store.ResolveByBuildIdAsync(release.BuildId, cancellationToken).ConfigureAwait(false);

        if (produced.Count == 0)
        {
            return ReleaseLineageWalk.Broken(
                hops,
                ReleaseLineageHopKind.Build,
                $"Build '{release.BuildId}' has no artifacts indexed in the store, so the release cannot be tied to a build output.");
        }

        if (manifest is not null && manifest.BuildId != release.BuildId)
        {
            return ReleaseLineageWalk.Broken(
                hops,
                ReleaseLineageHopKind.Build,
                $"The supplied build manifest is for {manifest.BuildId}, but the release names {release.BuildId}.");
        }

        hops.Add(new ReleaseLineageHop(
            ReleaseLineageHopKind.Build,
            release.BuildId.Value,
            $"{produced.Count} artifact(s) produced"));

        // ---- 4. The source commit ---------------------------------------------------------------------
        var commits = manifest is not null
            ? manifest.Identity.Sources.Select(s => s.CommitSha).ToArray()
            : [.. release.Identity.SourceCommits];

        if (commits.Length == 0)
        {
            return ReleaseLineageWalk.Broken(
                hops,
                ReleaseLineageHopKind.SourceCommit,
                "No source commit is recorded, so nothing ties the build to a governed revision.");
        }

        // A release that recorded a commit the build manifest does not carry is a real disagreement, not a
        // formatting difference, and it stops the walk.
        if (manifest is not null)
        {
            var manifestCommits = manifest.Identity.Sources.Select(s => s.CommitSha).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unrecorded = release.Identity.SourceCommits.FirstOrDefault(c => !manifestCommits.Contains(c));

            if (unrecorded is not null)
            {
                return ReleaseLineageWalk.Broken(
                    hops,
                    ReleaseLineageHopKind.SourceCommit,
                    $"{releaseId} names source commit {unrecorded}, which the build manifest does not record.");
            }
        }

        hops.Add(new ReleaseLineageHop(
            ReleaseLineageHopKind.SourceCommit,
            commits[0],
            manifest is not null ? "verified against the build manifest" : "from the release record; manifest not supplied"));

        foreach (var extra in commits.Skip(1))
        {
            hops.Add(new ReleaseLineageHop(ReleaseLineageHopKind.SourceCommit, extra, commits.Length > 1 ? "additional source revision" : null));
        }

        // ---- 5. The originating governed work ----------------------------------------------------------
        if (_lineageLog is null)
        {
            return ReleaseLineageWalk.Broken(
                hops,
                ReleaseLineageHopKind.GovernedWork,
                "No release lineage ledger is configured, so the outermost link cannot be resolved.");
        }

        var lineage = await _lineageLog.FindByReleaseIdAsync(releaseId, cancellationToken).ConfigureAwait(false);
        var work = lineage?.OriginatingWork;

        if (work is null)
        {
            // "There is no ledger" and "the ledger has no record for this release" are different findings
            // with different remedies, so they are reported differently. Conflating them would send someone
            // to configure a ledger that is already configured.
            return ReleaseLineageWalk.Broken(
                hops,
                ReleaseLineageHopKind.GovernedWork,
                lineage is null
                    ? $"The lineage ledger holds no record for {releaseId}, so the governed work it was performed for is unknown."
                    : $"The lineage record for {releaseId} names no governed work.");
        }

        hops.Add(new ReleaseLineageHop(ReleaseLineageHopKind.GovernedWork, work.WorkReference, work.Title));

        // The chain reads outermost-first in the hop list's canonical order; the walk discovered it
        // innermost-first. Reversing here is what makes the two orders one order.
        var ordered = hops
            .OrderBy(h => h.Kind)
            .ToList();

        return ReleaseLineageWalk.Complete(ordered);
    }
}
