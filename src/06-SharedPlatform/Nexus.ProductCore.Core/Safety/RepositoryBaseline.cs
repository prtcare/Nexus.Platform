// RepositoryBaseline.cs — W1-04 BASESHA BINDING.
//
// THE FIELD THAT DOES NOT EXIST
// -----------------------------
// V3_M1D_BASELINE_BINDING.md §1 measured it directly: `BaseSHA`, `BaseSha`, `BaseCommit` and
// `BaselineSha` occur ZERO times as code identifiers in either host. Forge's CreateWorktree
// cuts from whatever HEAD happens to be and never records the base commit; it holds no
// persisted worktree record at all. Developer's ActiveChange carries Branch and Worktree as
// free strings and nothing else.
//
// The consequence is stated in the same document: V3_M1C_LINEAGE.md — "which task introduced
// this change?" — is unimplementable without it, because the question requires knowing where
// the branch started.
//
// THE RULE THAT MATTERS
// ---------------------
// Capturing a SHA is easy and nearly useless on its own. What makes it a safety property is
// that the captured value CONSTRAINS the run afterwards:
//
//   * a BaseSHA that can silently move under a running change is not a baseline, it is a
//     timestamp;
//   * a run whose worktree HEAD has moved must become STALE and require an explicit act,
//     not continue quietly against a base nobody recorded;
//   * and when the answer is not knowable, the answer is UNKNOWN — which gates as STALE
//     (SG-2). A missing input is never a pass.
using System.Security.Cryptography;
using System.Text;

namespace Nexus.DevelopmentControl.Safety;

/// <summary>
/// The six-field binding, per repository (V3_M1D_BASELINE_BINDING.md §0). Recorded at
/// reservation time and immutable for the life of the run.
/// </summary>
public sealed record RepositoryBaseline(
    string RepositoryId,
    string Branch,
    string WorktreePath,
    string BaseSHA,
    string? UpstreamRef = null,
    string? UpstreamSHAAtStart = null,
    DateTimeOffset? CapturedAtUtc = null,
    /// <summary>BB-5: a dirty base is RECORDED, not ignored. A run that started on top of
    /// uncommitted work cannot be reproduced from BaseSHA alone, and saying so is cheaper than
    /// discovering it during a bisect.</summary>
    bool BaseWasDirty = false,
    string? DirtyFingerprint = null)
{
    public string WorktreeKey => NormalizeWorktree(WorktreePath);

    internal static string NormalizeWorktree(string? p) =>
        (p ?? "").Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();

    public string Fingerprint()
    {
        var s = string.Join("|", RepositoryId, Branch, WorktreeKey, BaseSHA, UpstreamRef ?? "", UpstreamSHAAtStart ?? "", BaseWasDirty ? "dirty" : "clean");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16];
    }
}

public enum BaselineState
{
    /// <summary>HEAD is exactly the recorded base. The run may proceed.</summary>
    Bound = 0,

    /// <summary>HEAD moved. The run is stale and needs an explicit act (BB-3).</summary>
    HeadMoved = 1,

    /// <summary>The observer is looking at a different worktree than the run was bound to.</summary>
    WorktreeMismatch = 2,

    /// <summary>Different repository entirely.</summary>
    RepositoryMismatch = 3,

    /// <summary>Upstream advanced, but the delta does not touch this run's declared scope.</summary>
    UpstreamMovedOutsideScope = 4,

    /// <summary>Upstream advanced and the delta intersects the scope, or the intersection
    /// cannot be determined.</summary>
    Stale = 5,

    /// <summary>An input required to decide is missing. Gates as STALE (SG-2).</summary>
    Unknown = 6,
}

public sealed record BaselineVerdict(
    BaselineState State,
    string Reason,
    IReadOnlyList<string> Evidence)
{
    /// <summary>Only two states permit the run to continue. Everything else — including
    /// UNKNOWN — stops it.</summary>
    public bool RunMayProceed =>
        State is BaselineState.Bound or BaselineState.UpstreamMovedOutsideScope;

    public bool RequiresExplicitAction =>
        State is BaselineState.HeadMoved or BaselineState.Stale or BaselineState.Unknown;
}

public static class BaselineBinding
{
    /// <summary>
    /// Evaluates a run against its recorded baseline.
    /// </summary>
    /// <param name="recorded">What the reservation captured.</param>
    /// <param name="observed">What the worktree reports now. <c>null</c> when the worktree
    /// cannot be read — which yields UNKNOWN, never Bound.</param>
    /// <param name="upstreamShaNow">The upstream tip now, or <c>null</c> if not read.</param>
    /// <param name="upstreamDeltaPaths">Paths the upstream gained since the base. <c>null</c>
    /// when the delta was not computed.</param>
    /// <param name="scope">This run's declared scope.</param>
    public static BaselineVerdict Evaluate(
        RepositoryBaseline? recorded,
        RepositoryBaseline? observed,
        string? upstreamShaNow = null,
        IReadOnlyList<string>? upstreamDeltaPaths = null,
        IEnumerable<ScopeItem>? scope = null)
    {
        if (recorded is null)
            return new BaselineVerdict(BaselineState.Unknown,
                "No baseline was recorded for this run, so there is nothing to bind it to.",
                new[] { "recorded=null" });

        if (string.IsNullOrWhiteSpace(recorded.BaseSHA))
            return new BaselineVerdict(BaselineState.Unknown,
                $"The baseline for '{recorded.RepositoryId}' carries no BaseSHA.",
                new[] { $"repository={recorded.RepositoryId}", $"branch={recorded.Branch}" });

        if (observed is null)
            return new BaselineVerdict(BaselineState.Unknown,
                "The current worktree state could not be read, so the baseline cannot be confirmed.",
                new[] { $"recordedBase={recorded.BaseSHA}", $"worktree={recorded.WorktreePath}" });

        var ev = new List<string>
        {
            $"repository={recorded.RepositoryId}",
            $"recordedBase={recorded.BaseSHA}",
            $"recordedWorktree={recorded.WorktreeKey}",
            $"observedHead={observed.BaseSHA}",
            $"observedWorktree={observed.WorktreeKey}",
        };

        if (!string.Equals(recorded.RepositoryId, observed.RepositoryId, StringComparison.OrdinalIgnoreCase))
            return new BaselineVerdict(BaselineState.RepositoryMismatch,
                $"The run is bound to repository '{recorded.RepositoryId}' but the observer is in '{observed.RepositoryId}'.",
                ev);

        if (!string.Equals(recorded.WorktreeKey, observed.WorktreeKey, StringComparison.Ordinal))
            return new BaselineVerdict(BaselineState.WorktreeMismatch,
                $"The run is bound to worktree '{recorded.WorktreePath}' but the observer is in '{observed.WorktreePath}'. " +
                "The same commit checked out twice is still two working trees, and edits in one are invisible to the other.",
                ev);

        if (!string.Equals(recorded.BaseSHA, observed.BaseSHA, StringComparison.OrdinalIgnoreCase))
            return new BaselineVerdict(BaselineState.HeadMoved,
                $"HEAD is {observed.BaseSHA} but the run was bound to {recorded.BaseSHA}. The run is stale and requires an explicit decision.",
                ev);

        // ---- upstream drift
        if (string.IsNullOrWhiteSpace(recorded.UpstreamRef) || string.IsNullOrWhiteSpace(upstreamShaNow))
        {
            ev.Add("upstream=not-observed");
            return new BaselineVerdict(BaselineState.Unknown,
                "The upstream state was not observed, so drift cannot be ruled out. An unobserved input is not a clean one.",
                ev);
        }

        if (string.Equals(recorded.UpstreamSHAAtStart, upstreamShaNow, StringComparison.OrdinalIgnoreCase))
            return new BaselineVerdict(BaselineState.Bound,
                "HEAD is the recorded base and the upstream has not moved.", ev);

        ev.Add($"upstreamAtStart={recorded.UpstreamSHAAtStart ?? "(none)"} upstreamNow={upstreamShaNow}");

        if (upstreamDeltaPaths is null)
        {
            ev.Add("upstreamDelta=not-computed");
            return new BaselineVerdict(BaselineState.Unknown,
                "The upstream has moved but the delta could not be computed, so whether it intersects this run's scope is unknown.",
                ev);
        }

        var scopeItems = (scope ?? Array.Empty<ScopeItem>()).ToArray();
        if (scopeItems.Length == 0)
        {
            ev.Add("scope=empty");
            return new BaselineVerdict(BaselineState.Unknown,
                "The upstream has moved and this run declares no scope, so no intersection can be computed. An empty scope is not an empty risk.",
                ev);
        }

        var hits = upstreamDeltaPaths
            .Where(p => scopeItems.Any(s => ChangeScopeCollision.PathsIntersect(
                s, new ScopeItem(ScopeKind.ExactFile, recorded.RepositoryId, p, AccessMode.Write))))
            .ToArray();

        if (hits.Length == 0)
            return new BaselineVerdict(BaselineState.UpstreamMovedOutsideScope,
                $"The upstream moved ({upstreamDeltaPaths.Count} paths) but none falls inside the declared scope. The run may proceed.",
                ev);

        ev.AddRange(hits.Select(h => "intersects=" + h));
        return new BaselineVerdict(BaselineState.Stale,
            $"The upstream moved and {hits.Length} of its changes fall inside this run's scope ({string.Join(", ", hits.Take(5))}). Rebasing is required before continuing.",
            ev);
    }

    /// <summary>Captures a baseline from observed repository facts. Kept explicit rather than
    /// reading git here, so the type stays IO-free and testable with synthetic histories.</summary>
    public static RepositoryBaseline Capture(
        string repositoryId, string branch, string worktreePath, string headSha,
        string? upstreamRef, string? upstreamSha, bool dirty, string? dirtyFingerprint,
        DateTimeOffset? now = null) =>
        new(repositoryId, branch, worktreePath, headSha, upstreamRef, upstreamSha,
            now ?? DateTimeOffset.UtcNow, dirty, dirtyFingerprint);
}
