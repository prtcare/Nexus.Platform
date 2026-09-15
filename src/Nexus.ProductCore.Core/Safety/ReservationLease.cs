// ReservationLease.cs — W1-03 REAL LEASE + HEARTBEAT + EXPIRY.
//
// THE DEFECT THIS REPLACES
// ------------------------
// V3_M1C_RESERVATION_LOCKING.md §5.1, verified in code: Developer's RecordHeartbeatAsync is
// documented as extending "a live activity's heartbeat so its lease/expiry does not lapse",
// and it does not. ExcelDevelopmentControlStore.cs:319-336 appends a new Activity Log row
// with Operation = "Heartbeat" and never updates ActiveChange.LastHeartbeat.
// LastHeartbeat is written exactly once, at reservation time. Nothing reads it. Nothing
// sweeps. ReserveWorkItemAsync writes Status "Open -- reserved" and no process ever
// reclaims it. Forge has the mirror image: a `Last Heartbeat` workbook column with no lease,
// TTL or renewal logic behind it.
//
// The consequence is that a worker which crashes mid-task leaves a reservation that is Open
// forever, excludes its node from selection forever, and is never reported as stale by
// anything at all.
//
// A LEASE IS A STATE, NOT A LOG ROW
// ---------------------------------
// Two rules make this type behave differently from what it replaces:
//
//   * EXPIRED is COMPUTED, never written (RL-9). Status is a function of
//     (recorded fields, now). No process has to run for a lease to expire, so there is no
//     sweeper to forget to start and no window in which a dead holder still reads as live.
//   * PID is NEVER a liveness test (RL-11). Process.GetProcessById on a recycled PID is a
//     false positive; on another host it is a false negative. ProcessStartTime and
//     HostBootId are recorded as CORROBORATING EVIDENCE for a decision already made on
//     lease grounds — they are never the decision.
//
// A heartbeat emitted by the thread doing the work stops during the work (L-6), which is
// exactly when the lease must not lapse. Callers are expected to drive Heartbeat from an
// independent watchdog; this type does not care who calls it, only that the resulting state
// is written where the next reader will see it.
using System.Security.Cryptography;
using System.Text;

namespace Nexus.DevelopmentControl.Safety;

/// <summary>Recorded state. This is mutable state in the store; it is NOT the computed state.</summary>
public enum ReservationState
{
    Active = 0,
    Released = 1,
    Revoked = 2,
}

/// <summary>Computed state — a function of the recorded fields and the current instant.
/// Nothing writes these values into the store (RL-9).</summary>
public enum ReservationStatus
{
    /// <summary>Held, lease unexpired.</summary>
    Active = 0,

    /// <summary>Held, lease unexpired, but no heartbeat for >= 3 heartbeat intervals. REPORTING
    /// ONLY — a stale-looking holder is never auto-reclaimed, because a long build emits no
    /// heartbeat while it runs (L-3, reclaim case 2).</summary>
    Stale = 1,

    /// <summary>Lease expired. No longer blocks others; may become a reclaim candidate.</summary>
    Expired = 2,

    /// <summary>Expired and the recorded host has demonstrably rebooted, so the holder cannot
    /// still be running. Safe to reclaim subject to the worktree guard.</summary>
    ReclaimCandidate = 3,

    /// <summary>Reclaimed by a later holder. This record is evidence and is never deleted.</summary>
    Reclaimed = 4,

    Released = 5,
    Revoked = 6,
}

/// <summary>
/// The ownership composite. A PID is not an identity; these ten fields together are
/// (V3_M1D_RESERVATION_POLICY.md §2). <see cref="LeaseEpoch"/> is the field that prevents
/// split-brain: it increments on every reclaim, and a holder presenting a lower epoch than
/// the recorded one is always rejected.
/// </summary>
public sealed record ReservationLease
{
    // ---- identity (immutable once written)
    public string ReservationId { get; init; } = "";
    public string WorkerId { get; init; } = "";
    public string HostId { get; init; } = "";
    public string HostBootId { get; init; } = "";
    public int ProcessId { get; init; }
    public DateTimeOffset ProcessStartTime { get; init; }

    // ---- binding (immutable once written)
    public string? BaseSHA { get; init; }
    public string? ScopeFingerprint { get; init; }
    public string? RepositoryId { get; init; }
    public string? Branch { get; init; }
    public string? Worktree { get; init; }

    // ---- lease (mutable)
    public long LeaseEpoch { get; init; } = 1;
    public DateTimeOffset LeaseStart { get; init; }
    public DateTimeOffset LastHeartbeat { get; init; }
    public DateTimeOffset Expiry { get; init; }
    public int RenewalCount { get; init; }
    public ReservationState State { get; init; } = ReservationState.Active;

    /// <summary>Monotonic-clock reading at <see cref="LastHeartbeat"/> (L-7). A wall clock can
    /// step backwards; a monotonic one cannot. Both are recorded because either alone can lie.</summary>
    public long LastHeartbeatMonotonicMs { get; init; }

    public string? ResultOrEvidence { get; init; }
    public string? ReclaimedFrom { get; init; }
    public string? ReclaimReason { get; init; }

    // ---- lease policy (L-1..L-5)
    public TimeSpan LeaseDuration { get; init; } = LeasePolicy.DefaultLease;
    public TimeSpan HeartbeatInterval { get; init; } = LeasePolicy.DefaultHeartbeatInterval;

    /// <summary>COMPUTED. Never written (RL-9).</summary>
    public ReservationStatus StatusAt(DateTimeOffset now)
    {
        if (State == ReservationState.Released) return ReservationStatus.Released;
        if (State == ReservationState.Revoked) return ReservationStatus.Revoked;
        if (ReclaimedFrom is not null && ReclaimReason is not null && State == ReservationState.Active && Expiry < now)
            return ReservationStatus.Reclaimed;

        if (Expiry >= now)
        {
            var sinceHeartbeat = now - LastHeartbeat;
            return sinceHeartbeat >= LeasePolicy.StaleAfter(HeartbeatInterval)
                ? ReservationStatus.Stale
                : ReservationStatus.Active;
        }

        // Expired. Whether it is reclaimABLE is a separate, host-aware question.
        return ReservationStatus.Expired;
    }

    /// <summary>True when the holder may renew. RL-12: only the holding WorkerId may renew.</summary>
    public bool MayRenew(string workerId, DateTimeOffset now) =>
        State == ReservationState.Active
        && string.Equals(WorkerId, workerId, StringComparison.Ordinal)
        && Expiry >= now
        && RenewalCount < LeasePolicy.MaxRenewals;

    /// <summary>RL-13: renewal is bounded. A lease that can be renewed forever is not a lease.</summary>
    public bool IsRenewalExhausted => RenewalCount >= LeasePolicy.MaxRenewals;

    public static string NewReservationId(DateTimeOffset now, int sequence) =>
        $"RSV-{now:yyyyMMdd}-{sequence:D3}";
}

public static class LeasePolicy
{
    // L-1..L-5. Every number is declared here rather than inline, so a policy change is one
    // edit and every consumer moves together.
    public static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(5);
    public const int MaxRenewals = 96;                       // L-5: 96 x 15 min = 24 h
    public static readonly TimeSpan NoProgressCeiling = TimeSpan.FromHours(4); // L-8

    /// <summary>L-3: three missed heartbeats is the REPORTING threshold, never a reclaim trigger.</summary>
    public static TimeSpan StaleAfter(TimeSpan heartbeatInterval) =>
        TimeSpan.FromTicks(heartbeatInterval.Ticks * 3);

    public static ReservationLease NewLease(
        string reservationId, string workerId, IHostIdentity host,
        string? repositoryId = null, string? branch = null, string? worktree = null,
        string? baseSha = null, string? scopeFingerprint = null,
        TimeSpan? leaseDuration = null, DateTimeOffset? now = null)
    {
        var t = now ?? DateTimeOffset.UtcNow;
        var duration = leaseDuration ?? DefaultLease;
        return new ReservationLease
        {
            ReservationId = reservationId,
            WorkerId = workerId,
            HostId = host.HostId,
            HostBootId = host.HostBootId,
            ProcessId = host.ProcessId,
            ProcessStartTime = host.ProcessStartTime,
            RepositoryId = repositoryId,
            Branch = branch,
            Worktree = worktree,
            BaseSHA = baseSha,
            ScopeFingerprint = scopeFingerprint,
            LeaseStart = t,
            LastHeartbeat = t,
            Expiry = t + duration,
            LeaseEpoch = 1,
            RenewalCount = 0,
            State = ReservationState.Active,
            LeaseDuration = duration,
            LastHeartbeatMonotonicMs = Environment.TickCount64,
        };
    }

    /// <summary>Extends the lease. The RETURNED value is the effective lease state; a caller
    /// that only writes a log row has not heartbeated.</summary>
    public static ReservationLease Renew(ReservationLease lease, string workerId, DateTimeOffset now, TimeSpan? extend = null)
    {
        if (!lease.MayRenew(workerId, now))
            throw new StoreIdentityException(
                $"Reservation {lease.ReservationId} may not be renewed by '{workerId}' " +
                $"(holder='{lease.WorkerId}', state={lease.State}, expiry={lease.Expiry:o}, renewals={lease.RenewalCount}).");

        return lease with
        {
            LastHeartbeat = now,
            Expiry = now + (extend ?? lease.LeaseDuration),
            RenewalCount = lease.RenewalCount + 1,
            LastHeartbeatMonotonicMs = Environment.TickCount64,
        };
    }
}

// =====================================================================================
// Reclaim — the five documented cases
// =====================================================================================

public enum ReclaimAction
{
    /// <summary>Nothing to reclaim; a live holder exists.</summary>
    None = 0,

    /// <summary>Take it. The holder provably cannot still be running.</summary>
    Reclaim = 1,

    /// <summary>ESCALATE TO A HUMAN. Reservation policy case 5 — never automatic.</summary>
    Escalate = 2,

    /// <summary>The request is invalid; do not reclaim and do not retry blindly.</summary>
    Reject = 3,
}

/// <summary>Whether the branch/worktree a reservation names still holds uncommitted work.
/// Reclaim case 5 turns on this, and it is a git question — so it is injected rather than
/// guessed. <see cref="Unknown"/> MUST gate as ESCALATE: a missing input is never a pass
/// (V3_M1D_BASELINE_BINDING.md SG-2).</summary>
public enum WorktreeActivity { Unknown = 0, Clean = 1, HoldsUncommittedWork = 2 }

public interface IReservationReclaimGuard
{
    WorktreeActivity CheckWorktree(ReservationLease holder);
}

/// <summary>The conservative default: it never claims to know. With no guard supplied, every
/// otherwise-reclaimable lease escalates to a human instead of being stolen.</summary>
public sealed class UnknownWorktreeGuard : IReservationReclaimGuard
{
    public static readonly UnknownWorktreeGuard Instance = new();
    public WorktreeActivity CheckWorktree(ReservationLease holder) => WorktreeActivity.Unknown;
}

public sealed record ReclaimDecision(
    ReclaimAction Action,
    string Reason,
    long NewEpoch,
    IReadOnlyList<string> Evidence);

public static class ReservationLeasePolicy
{
    /// <summary>
    /// The deterministic reclaim decision (V3_M1D_RESERVATION_POLICY.md §4.1). Order matters:
    /// the host-identity cases are decided BEFORE the lease is considered, because a lease that
    /// looks unexpired on a machine that has since rebooted is not evidence of a live worker.
    /// </summary>
    public static ReclaimDecision EvaluateReclaim(
        ReservationLease? holder, DateTimeOffset now, IHostIdentity callerHost,
        IReservationReclaimGuard? guard = null)
    {
        // No recorded holder: the lock file exists but says nothing. Treat as reclaimable —
        // an absent payload is not an absent holder, so the caller has already established
        // by exclusive handle that nobody holds it.
        if (holder is null)
            return new ReclaimDecision(ReclaimAction.Reclaim,
                "No holder is recorded and no process holds an open handle; the lock is a leftover.",
                NewEpoch: 1,
                new[] { $"callerHost={callerHost.HostId}", "holder=none" });

        var evidence = new List<string>
        {
            $"holderReservation={holder.ReservationId}",
            $"holderWorker={holder.WorkerId}",
            $"holderHost={holder.HostId}",
            $"holderBootId={holder.HostBootId}",
            $"holderLeaseStart={holder.LeaseStart:o}",
            $"holderExpiry={holder.Expiry:o}",
            $"holderRenewals={holder.RenewalCount}",
            $"holderEpoch={holder.LeaseEpoch}",
            $"now={now:o}",
            $"callerHost={callerHost.HostId}",
        };

        // ---- case 0: the holder RELINQUISHED the lease. Decided before every liveness case,
        // because a released lease is not a held one however recently it was heartbeated:
        // without this, a process that acquires, releases and immediately re-acquires is
        // refused by its OWN record — it reads an unexpired, freshly heartbeated lease and
        // answers case 1. The state is written only through the held exclusive handle, so a
        // `Released` record cannot be forged by a process that does not hold the lock.
        if (holder.State == ReservationState.Released)
        {
            evidence.Add($"case=0-released reservation={holder.ReservationId} worker={holder.WorkerId}");
            return new ReclaimDecision(ReclaimAction.Reclaim,
                $"Reservation {holder.ReservationId} was explicitly released by {holder.WorkerId}; the lock is free.",
                holder.LeaseEpoch + 1, evidence);
        }

        // ---- case 4: machine reboot. The recorded boot id is not this boot's, so the process
        // that wrote this lease cannot exist any more. Decided WITHOUT inspecting a PID.
        if (!string.IsNullOrEmpty(holder.HostBootId)
            && string.Equals(holder.HostId, callerHost.HostId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(holder.HostBootId, callerHost.HostBootId, StringComparison.Ordinal))
        {
            evidence.Add($"case=4-machine-reboot recordedBoot={holder.HostBootId} currentBoot={callerHost.HostBootId}");
            return Guarded(holder, ReclaimAction.Reclaim, "The recorded host has rebooted since the lease was taken.", evidence, guard);
        }

        // ---- lease still live. Two sub-cases, and they are NOT the same decision.
        if (holder.Expiry >= now)
        {
            var sinceHeartbeat = now - holder.LastHeartbeat;
            var stale = sinceHeartbeat >= LeasePolicy.StaleAfter(holder.HeartbeatInterval);

            if (!stale)
            {
                evidence.Add($"case=1-worker-alive heartbeatAge={sinceHeartbeat.TotalSeconds:F1}s");
                return new ReclaimDecision(ReclaimAction.None,
                    $"The lease is live until {holder.Expiry:o}; it was heartbeated {sinceHeartbeat.TotalSeconds:F1}s ago.",
                    holder.LeaseEpoch, evidence);
            }

            // ---- case 2: no heartbeat, but the lease has not lapsed. NEVER auto-reclaim:
            // a long build emits no heartbeat while it runs (L-3, L-6). This is the case that
            // protects 40-minute compiles from being stolen by a 90-second staleness rule.
            evidence.Add($"case=2-stale-live heartbeatAge={sinceHeartbeat.TotalSeconds:F1}s leaseRemaining={(holder.Expiry - now).TotalSeconds:F1}s");
            return new ReclaimDecision(ReclaimAction.None,
                $"STALE_LIVE: no heartbeat for {sinceHeartbeat.TotalSeconds:F0}s but the lease is good for another " +
                $"{(holder.Expiry - now).TotalMinutes:F1} minutes. Not reclaimed — a running build emits no heartbeat.",
                holder.LeaseEpoch, evidence);
        }

        // ---- the lease has expired. Continue to the host-identity checks.
        evidence.Add($"expiredFor={(now - holder.Expiry).TotalSeconds:F1}s");

        // ---- case 3: PID reuse. A different process start time for the same PID means the
        // recorded process is gone and its PID has been handed to somebody else. This is
        // CORROBORATION: it can only ever make reclaim MORE certain, never less.
        if (!string.IsNullOrEmpty(holder.HostBootId)
            && string.Equals(holder.HostId, callerHost.HostId, StringComparison.OrdinalIgnoreCase)
            && holder.ProcessId > 0
            && SameProcessStart(holder, callerHost, out var liveStart))
        {
            evidence.Add($"case=3-pid-reuse pid={holder.ProcessId} recordedStart={holder.ProcessStartTime:o} liveStart={liveStart:o}");
            return Guarded(holder, ReclaimAction.Reclaim,
                $"PID {holder.ProcessId} is still running but started at {liveStart:o}, not {holder.ProcessStartTime:o}: the PID was reused.",
                evidence, guard);
        }

        // ---- case 2b: the recorded host is NOT this host, so we cannot observe it at all.
        // We do not assume it is dead. Escalate rather than steal.
        if (!string.Equals(holder.HostId, callerHost.HostId, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add($"case=2b-foreign-host recorded={holder.HostId} caller={callerHost.HostId}");
            return new ReclaimDecision(ReclaimAction.Escalate,
                $"The lease expired, but it was taken on host '{holder.HostId}' and this is '{callerHost.HostId}'. " +
                "This host cannot observe that one, so it does not assume it is dead.",
                holder.LeaseEpoch + 1, evidence);
        }

        // ---- case 5: same host, lease expired. Before taking it, ask whether the work it
        // claimed is still sitting in a branch or worktree. THIS IS A HARD STOP, not a warning
        // (V3_M1D_RESERVATION_POLICY.md §4.1 step 4).
        return Guarded(holder, ReclaimAction.Reclaim,
            $"The lease expired {(now - holder.Expiry).TotalMinutes:F1} minutes ago on this host.",
            evidence, guard);
    }

    private static bool SameProcessStart(ReservationLease holder, IHostIdentity callerHost, out DateTimeOffset liveStart)
    {
        try
        {
            liveStart = System.Diagnostics.Process.GetProcessById(holder.ProcessId).StartTime.ToUniversalTime();
            // Start times are recorded from the same API, so compare with a tolerance for
            // filesystem timestamp granularity rather than demanding tick equality.
            var delta = (liveStart - holder.ProcessStartTime).Duration();
            return delta > TimeSpan.FromSeconds(2);
        }
        catch (ArgumentException)
        {
            // No such process. That is also fine — but it is NOT what this branch decides;
            // the PID-reuse claim requires a live PID with a different start time.
            liveStart = default;
            return false;
        }
        catch (Exception)
        {
            liveStart = default;
            return false;
        }
    }

    /// <summary>
    /// Case 5's gate. Called only when reclaim is otherwise justified, so the worktree
    /// question is never skipped and never decides in favour of stealing.
    /// </summary>
    private static ReclaimDecision Guarded(
        ReservationLease holder, ReclaimAction proposed, string reason,
        List<string> evidence, IReservationReclaimGuard? guard)
    {
        var activity = (guard ?? UnknownWorktreeGuard.Instance).CheckWorktree(holder);
        evidence.Add($"case=5-worktree-guard activity={activity} branch={holder.Branch ?? "(none)"} worktree={holder.Worktree ?? "(none)"}");

        return activity switch
        {
            WorktreeActivity.Clean => new ReclaimDecision(proposed, reason, holder.LeaseEpoch + 1, evidence),

            WorktreeActivity.HoldsUncommittedWork => new ReclaimDecision(
                ReclaimAction.Escalate,
                $"ESCALATED: {reason} But branch '{holder.Branch}' / worktree '{holder.Worktree}' still holds " +
                "uncommitted work. Automatic reclaim would silently discard it, so this requires a human " +
                "decision (reservation policy case 5).",
                holder.LeaseEpoch + 1, evidence),

            // Unknown is NOT clean. A guard that could not answer has not answered "safe".
            _ => new ReclaimDecision(
                ReclaimAction.Escalate,
                $"ESCALATED: {reason} The state of branch '{holder.Branch}' / worktree '{holder.Worktree}' " +
                "could not be established, and an unestablished input is never a pass.",
                holder.LeaseEpoch + 1, evidence),
        };
    }

    /// <summary>Stable fingerprint of a declared scope, bound into the reservation so a scope
    /// that changes under a live reservation is detectable rather than silently permitted
    /// (V3_M1C_RESERVATION_LOCKING.md field 10 / field 17).</summary>
    public static string ScopeFingerprint(IEnumerable<ScopeItem> scope)
    {
        var lines = scope
            .Select(s => s.Canonical())
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        var joined = string.Join("\n", lines);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }
}
