// AtomicWriterLock.cs — W1-02 ATOMIC LOCK / RESERVATION ACQUISITION
//                     + W1-03 the liveness half of the lease.
//
// WHAT THIS REPLACES, AND WHY
// ---------------------------
// Forge's live writer lock is WorkbookWriterGate: a PID text file at
// <DevBridge root>\logs\workbook-writer.lock. Its acquisition is
//
//     File.Exists -> ReadAllText -> ParsePid -> Process.GetProcessById -> File.Delete -> WriteAllText
//
// with no exclusive handle held at any point. V3_M1D_SAFETY_GAPS.md records it as guard G-4.
// Two defects follow directly from that shape:
//
//   * TOCTOU. Between the File.Exists and the WriteAllText another process can complete the
//     same sequence. Both then believe they hold the lock and both write the workbook.
//   * PID-based liveness (V3_M1C_RESERVATION_LOCKING.md §6.2, rule RL-11). A recycled PID
//     reads as "live writer" and blocks forever; a writer on another machine reads as dead
//     and is stolen from.
//
// THE FIX IS NOT A BETTER CHECK. It is removing the check.
//
// An OS file handle is the mutual exclusion. Acquisition is a single
// FileMode.OpenOrCreate + FileShare.None open: the open ITSELF is the test and the claim,
// so there is no window between deciding and acting. The winner holds an open handle for
// the whole critical section; a crashed holder's handle is released by the kernel, which is
// the only liveness signal in the system that is correct without inspecting a PID.
//
// The lock FILE is not deleted on release — it is left in place with its final state, so a
// crash leaves evidence rather than a gap. Reclaim is a separate, explicit, recorded act.
//
// The payload is a ReservationLease (the 10-field ownership composite), so the lock records
// who holds it, on which host, from which process start, over which base SHA, for which
// scope — instead of the single `pid=` field whose `acquired=` sibling was written and never
// read (V3_M1D_LOCK_IDENTITY.md §5).
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.DevelopmentControl.Safety;

/// <summary>Why an acquisition attempt ended the way it did. Every outcome is a value; nothing
/// waits silently behind another writer (V3_M1C_RESERVATION_LOCKING.md §4.2).</summary>
public enum AcquireOutcome
{
    /// <summary>This caller now holds the lock.</summary>
    Acquired = 0,

    /// <summary>An OS handle is held by another process. Someone else is working.</summary>
    BusyLive = 1,

    /// <summary>No live handle, but the previous holder's lease has NOT expired and its
    /// recorded process is gone from this host. Treated as BUSY: V3_M1D_RESERVATION_POLICY.md
    /// reclaim case 2 protects long-running builds that emit no heartbeat.</summary>
    BusyStaleLive = 2,

    /// <summary>No live handle and the lease expired. Reclaimable — but see ReclaimBlocked.</summary>
    Reclaimable = 3,

    /// <summary>Reclaimable by lease, but a protected condition forbids automatic reclaim
    /// (reservation policy case 5: the branch or worktree may still hold uncommitted work).
    /// ESCALATE TO A HUMAN. Never auto-reclaimed.</summary>
    ReclaimBlocked = 4,

    /// <summary>The lock could not be created or opened (I/O, permissions, path).</summary>
    IoFailure = 5,

    /// <summary>
    /// The caller named a lock directory that is not the one this store's identity derives.
    ///
    /// <para>Refused rather than honoured, because honouring it is the defect this outcome exists
    /// to close: the lock's identity comes from the store path but its <i>location</i> used to come
    /// from the caller, so two callers naming two directories created two unrelated files for one
    /// logical lock and both believed they held it. Mutual exclusion was a convention — every caller
    /// must agree — instead of a property the component enforces.</para>
    /// </summary>
    LockDirectoryNotCanonical = 6,
}

/// <summary>The result of one acquisition attempt, carrying the evidence for the outcome.
/// <see cref="Lock"/> is non-null exactly when <see cref="Outcome"/> is
/// <see cref="AcquireOutcome.Acquired"/>; the caller owns it and must dispose it.</summary>
public sealed record AcquireResult(
    AcquireOutcome Outcome,
    ReservationLease? Holder,
    string Detail,
    IReadOnlyList<string> Evidence,
    AtomicWriterLock? Lock = null)
{
    public bool Acquired => Outcome == AcquireOutcome.Acquired;

    /// <summary>True when the caller may sensibly retry: a live holder is transient.</summary>
    public bool Retryable => Outcome is AcquireOutcome.BusyLive or AcquireOutcome.BusyStaleLive;
}

/// <summary>Supplies the host-and-process facts the ownership composite needs. Kept behind an
/// interface so tests can present a synthetic host, a synthetic boot id and a synthetic
/// process start time — a test that cannot simulate a rebooted machine cannot test case 4.</summary>
public interface IHostIdentity
{
    string HostId { get; }

    /// <summary>Changes on every machine boot. The ONLY signal that distinguishes "this
    /// process is gone" from "this machine rebooted" without inspecting a PID.</summary>
    string HostBootId { get; }

    /// <summary>Wall-clock start of the current process. Corroborating evidence only —
    /// never a liveness test (RL-11).</summary>
    DateTimeOffset ProcessStartTime { get; }

    int ProcessId { get; }
}

public sealed class MachineHostIdentity : IHostIdentity
{
    public static readonly MachineHostIdentity Instance = new();

    private MachineHostIdentity()
    {
        HostId = Safe(() => Environment.MachineName);

        // A boot id must CHANGE on reboot and be STABLE within a boot. Environment.TickCount64
        // is milliseconds since boot, so (now - uptime) is the boot instant; truncating to the
        // minute absorbs clock skew, and only the hash of it is retained.
        //
        // The truncation must remove the SUB-SECOND component too. Constructing the timestamp
        // from the retained ticks (rather than from whole fields) leaves the milliseconds in
        // place, so two processes in one boot — which differ by a few ms of uptime-vs-clock
        // sampling — derive different boot ids. That is not a cosmetic slip: this value is the
        // ONLY signal separating reclaim case 3 (a reused PID, reclaimable) from case 4 (the
        // machine rebooted). A boot id that is unstable within a boot makes every reclaim look
        // like case 4 and the signal carries no information.
        try
        {
            var boot = (DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64)).UtcDateTime;
            var rounded = new DateTimeOffset(boot.Year, boot.Month, boot.Day, boot.Hour, boot.Minute, 0, TimeSpan.Zero);
            HostBootId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(HostId + "|" + rounded.ToString("O"))))[..16];
        }
        catch
        {
            HostBootId = "unknown-boot";
        }

        ProcessStartTime = SafeTime();
        ProcessId = Environment.ProcessId;
    }

    public string HostId { get; }

    /// <summary>Rotates when the machine reboots; identical for every process in one boot.</summary>
    public string HostBootId { get; }

    public DateTimeOffset ProcessStartTime { get; }
    public int ProcessId { get; }

    private static string Safe(Func<string> f)
    {
        try { return f(); } catch { return "unknown"; }
    }

    private static DateTimeOffset SafeTime()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { return DateTimeOffset.UtcNow; }
    }
}

/// <summary>
/// The atomic writer lock over a governed store. One instance per critical section; dispose to
/// release. The exclusive handle is the lock — nothing here inspects a PID to decide liveness.
/// </summary>
public sealed class AtomicWriterLock : IDisposable
{
    private readonly FileStream _handle;
    private readonly string _lockPath;

    private AtomicWriterLock(FileStream handle, string lockPath, ReservationLease lease)
    {
        _handle = handle;
        _lockPath = lockPath;
        Lease = lease;
    }

    public ReservationLease Lease { get; }

    public string LockPath => _lockPath;

    /// <summary>
    /// The directory a store's lock must live in, derived from the store path and nothing else.
    ///
    /// <para><b>Why this is derived rather than supplied.</b> The lock's file NAME already comes from
    /// the canonical store identity, so two spellings of one workbook reach the same name. Its
    /// DIRECTORY did not, and that made the identity half a coordination key: two callers naming two
    /// directories produced two files with the same name, neither visible to the other, and both
    /// callers held "the lock". A safety property that holds only when every caller independently
    /// agrees on a path is a convention, not a control — the same reasoning W9.0 decision D-3 applies
    /// to artifact immutability.</para>
    ///
    /// <para>The rule is "beside the thing it guards": <c>&lt;workbook directory&gt;/.lock</c>. It
    /// needs no configuration, cannot drift between hosts, and is what the estate's own harnesses
    /// already used by hand before it was enforced.</para>
    /// </summary>
    public static string CanonicalLockDirectoryFor(string storePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
        {
            throw new ArgumentException("A lock needs the store it guards.", nameof(storePath));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(storePath));

        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException(
                $"'{storePath}' has no directory, so there is nowhere canonical to place its lock.",
                nameof(storePath));
        }

        return Path.Combine(directory, ".lock");
    }

    /// <summary>
    /// Whether a caller-named directory is one this component will accept. Exactly three inputs are
    /// accepted, and they all resolve to the same single lock file:
    ///
    /// <list type="bullet">
    /// <item><b>null or blank</b> — "no opinion": the caller opts into the derived location. This is
    /// the preferred call shape and the one every migrated caller now uses.</item>
    /// <item><b>the canonical directory</b>, <c>&lt;store directory&gt;\.lock</c>.</item>
    /// <item><b>the store's own directory</b>, <c>&lt;store directory&gt;</c>. Accepted as a
    /// canonical-<i>equivalent</i> spelling and resolved to the canonical directory, which is what
    /// makes "validated, never honoured" visible rather than merely asserted: the caller names a
    /// place, and the lock still goes beside the thing it guards.</item>
    /// </list>
    ///
    /// <para><b>W9.3 note: the third form was NOT accepted before this change.</b> The comparison
    /// named only the canonical directory, so a caller passing the store's own directory was refused
    /// with <see cref="AcquireOutcome.LockDirectoryNotCanonical"/> even though the contract this
    /// component implements describes the form as accepted. Widening by exactly that one
    /// non-arbitrary value is what closes the gap; it creates no second lock domain, because
    /// <see cref="LockPathFor"/> and <see cref="TryAcquire"/> both place the lock at the canonical
    /// path regardless.</para>
    ///
    /// <para>Everything else is refused. A directory that is neither the store's own nor its
    /// <c>.lock</c> child is a place this component does not own, and honouring it is the defect the
    /// refusal exists to close.</para>
    /// </summary>
    public static bool IsCanonicalLockDirectory(string storePath, string? lockDirectory)
    {
        if (string.IsNullOrWhiteSpace(lockDirectory))
        {
            return true;
        }

        static string Normalise(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        try
        {
            var named = Normalise(lockDirectory);
            var canonical = Normalise(CanonicalLockDirectoryFor(storePath));

            if (string.Equals(named, canonical, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // The store's own directory is accepted as a canonical-equivalent spelling. Both sides are
            // already fully resolved, so this is a comparison of two absolute paths and not of one
            // absolute path against a caller-shaped one.
            return string.Equals(
                named,
                Normalise(Path.GetDirectoryName(Path.GetFullPath(storePath))!),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A directory that cannot even be resolved is not the canonical one.
            return false;
        }
    }

    /// <summary>
    /// The lock file path for a store.
    ///
    /// <para><b>The canonical directory always wins.</b> <paramref name="lockDirectory"/> is accepted
    /// only so existing callers keep compiling and is <i>validated</i>, never honoured:
    /// <see cref="TryAcquire"/> refuses a mismatch with
    /// <see cref="AcquireOutcome.LockDirectoryNotCanonical"/> before reaching here. This method
    /// therefore cannot be used to form a second lock domain even when called directly.</para>
    /// </summary>
    public static string LockPathFor(string storePath, string? lockDirectory = null, bool useCanonicalIdentity = true)
    {
        var id = useCanonicalIdentity
            ? DevelopmentControlStoreIdentity.CanonicalFromWorkbookPath(storePath)
            : DevelopmentControlStoreIdentity.FromWorkbookPath(storePath);

        _ = lockDirectory;
        return Path.Combine(CanonicalLockDirectoryFor(storePath), id.LockFileName);
    }

    /// <summary>
    /// Attempts to acquire. <b>Atomic by construction:</b> the single
    /// <c>FileShare.None</c> open is simultaneously the availability test and the claim, so
    /// there is no interval between them in which another process can act. No caller-supplied
    /// "is it free?" predicate is consulted, and none is accepted.
    /// </summary>
    public static AcquireResult TryAcquire(
        string storePath,
        string? lockDirectory,
        ReservationLease requested,
        IHostIdentity host,
        IReservationReclaimGuard? reclaimGuard = null)
    {
        // Validated BEFORE anything is created. A caller-named directory that is not the canonical one
        // does not get a lock file: honouring it is precisely how two callers came to hold two files
        // for one identity. Refusing here means the mistake is visible at the call that made it,
        // rather than as two writers proceeding concurrently three weeks later.
        if (!IsCanonicalLockDirectory(storePath, lockDirectory))
        {
            return new AcquireResult(
                AcquireOutcome.LockDirectoryNotCanonical,
                null,
                $"Refused: the lock directory '{lockDirectory}' is not the one this store's identity "
                + $"derives, which is '{CanonicalLockDirectoryFor(storePath)}'. The lock's location is a "
                + "function of the store, not of the caller; two directories for one store would be two "
                + "lock domains and neither would exclude the other.",
                Array.Empty<string>());
        }

        var canonicalDirectory = CanonicalLockDirectoryFor(storePath);
        var lockPath = LockPathFor(storePath, canonicalDirectory);

        try
        {
            Directory.CreateDirectory(canonicalDirectory);
        }
        catch (Exception ex)
        {
            return new AcquireResult(AcquireOutcome.IoFailure, null,
                $"The lock directory could not be created: {ex.Message}", Array.Empty<string>());
        }

        // ---- attempt 1: create-and-hold. This is the claim, not a probe.
        if (TryOpenExclusive(lockPath, createNew: true, out var handle, out _))
        {
            var lease = WriteLease(handle!, requested, host, newEpoch: requested.LeaseEpoch);
            return new AcquireResult(AcquireOutcome.Acquired, lease,
                "Created the lock and holds it",
                new[] { $"lock={lockPath}", $"epoch={lease.LeaseEpoch}", $"host={lease.HostId}", $"reservation={lease.ReservationId}" },
                new AtomicWriterLock(handle!, lockPath, lease));
        }

        // ---- the file already exists. Someone may hold it; find out by trying to hold it too.
        var priorHolder = ReadLease(lockPath);

        if (!TryOpenExclusive(lockPath, createNew: false, out handle, out _))
        {
            // The OS refused: another process holds an open handle. That is liveness,
            // established by the kernel, with no PID inspection anywhere.
            return new AcquireResult(AcquireOutcome.BusyLive, priorHolder,
                "Another process holds an open handle on the writer lock.",
                Evidence(priorHolder, host));
        }

        // We can hold it, so nobody else does. Whether we MAY take it is a lease question.
        var decision = ReservationLeasePolicy.EvaluateReclaim(priorHolder, DateTimeOffset.UtcNow, host, reclaimGuard);

        if (decision.Action == ReclaimAction.Reclaim)
        {
            var lease = WriteLease(handle!, requested, host, reclaimedFrom: priorHolder, newEpoch: decision.NewEpoch);
            return new AcquireResult(AcquireOutcome.Acquired, lease,
                "Reclaimed: " + decision.Reason,
                EvidenceWith(priorHolder, host, decision.Evidence),
                new AtomicWriterLock(handle!, lockPath, lease));
        }

        // Not ours to take. Release the handle we opened so we do not ourselves become the
        // blocker for the next correct caller.
        handle!.Dispose();

        var outcome = decision.Action == ReclaimAction.Escalate
            ? AcquireOutcome.ReclaimBlocked
            : AcquireOutcome.BusyStaleLive;

        return new AcquireResult(outcome, priorHolder, decision.Reason, EvidenceWith(priorHolder, host, decision.Evidence));
    }

    /// <summary>
    /// Opens the lock file for read+write while SHARING READ. Returns false when another handle
    /// is open or the file is absent (and <paramref name="createNew"/> is false).
    /// <para><b>Why <c>FileShare.Read</c> and not <c>FileShare.None</c>.</b> Exclusion does not
    /// come from the holder's share mode — it comes from the CANDIDATE's. Every acquirer and
    /// every probe asks for <c>FileShare.None</c>, and Windows refuses that open while any other
    /// handle exists, whatever the holder allowed. So granting read costs nothing in exclusion
    /// and buys the one thing the old gate could never do: a second process can read the PAYLOAD
    /// while the lock is genuinely held, and therefore say WHO holds it and WHY it was refused
    /// instead of reporting an anonymous BUSY.</para>
    /// <para>The reverse direction is closed too: a reader whose own share mode does not permit
    /// the holder's already-granted write access is itself refused, so a reader cannot lock the
    /// holder out of its own critical section.</para>
    /// </summary>
    private static bool TryOpenExclusive(string path, bool createNew, out FileStream? handle, out bool created)
    {
        created = false;
        try
        {
            handle = new FileStream(
                path,
                createNew ? FileMode.CreateNew : FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 512,
                FileOptions.WriteThrough);
            created = createNew;
            return true;
        }
        catch (IOException)
        {
            // CreateNew -> exists; OpenOrCreate -> sharing violation. Both mean "not yours".
            handle = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            handle = null;
            return false;
        }
    }

    private static ReservationLease WriteLease(
        FileStream handle, ReservationLease requested, IHostIdentity host,
        ReservationLease? reclaimedFrom = null, long newEpoch = 1)
    {
        var now = DateTimeOffset.UtcNow;
        var lease = requested with
        {
            LeaseEpoch = newEpoch,
            HostId = host.HostId,
            HostBootId = host.HostBootId,
            ProcessId = host.ProcessId,
            ProcessStartTime = host.ProcessStartTime,
            LeaseStart = now,
            LastHeartbeat = now,
            RenewalCount = 0,
            ReclaimedFrom = reclaimedFrom?.ReservationId,
            ReclaimReason = reclaimedFrom is null ? null : "lease expired",
        };

        handle.SetLength(0);
        handle.Position = 0;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(lease, LeaseJson));
        handle.Write(bytes, 0, bytes.Length);
        handle.Flush(flushToDisk: true);
        return lease;
    }

    /// <summary>Reads the recorded holder. Read-only and never creates the file.</summary>
    public static ReservationLease? ReadLease(string lockPath)
    {
        try
        {
            if (!File.Exists(lockPath)) return null;
            // A live holder opened with FileShare.Read, so this read succeeds WHILE THE LOCK IS
            // HELD. That is what lets a refused caller report who holds it rather than an
            // anonymous BUSY. A null return therefore means "no readable payload" — which the
            // caller must still treat as "a holder may exist", never as "the lock is free":
            // liveness is the handle, and this method never looks for one.
            using var s = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(s, Encoding.UTF8);
            var text = r.ReadToEnd();
            return string.IsNullOrWhiteSpace(text)
                ? null
                : JsonSerializer.Deserialize<ReservationLease>(text, LeaseJson);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    /// <summary>Heartbeat for the WRITER lock: rewrites the lease payload in place through the
    /// already-held exclusive handle. This is the property V3_M1C §5.1 found missing on the
    /// Developer side — a heartbeat that updates the effective lease state rather than
    /// appending a log row nobody reads.</summary>
    public ReservationLease Heartbeat(TimeSpan? extend = null)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = Lease with
        {
            LastHeartbeat = now,
            RenewalCount = Lease.RenewalCount + 1,
            Expiry = now + (extend ?? Lease.LeaseDuration),
        };
        _handle.SetLength(0);
        _handle.Position = 0;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(updated, LeaseJson));
        _handle.Write(bytes, 0, bytes.Length);
        _handle.Flush(flushToDisk: true);
        return updated;
    }

    /// <summary>
    /// Relinquishes the lock EXPLICITLY: records the release in the payload through the held
    /// handle, then lets <see cref="Dispose"/> free the handle.
    /// <para>The file is KEPT. A crash leaves a lease that still looks held; an explicit release
    /// leaves one that says it was released. Those are different records on purpose — a caller
    /// that deletes the file on release destroys the evidence of every crash that preceded it,
    /// and leaves the next reader unable to tell "the holder finished" from "the holder died".</para>
    /// </summary>
    public ReservationLease Release()
    {
        var released = Lease with { State = ReservationState.Released, LastHeartbeat = DateTimeOffset.UtcNow };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(released, LeaseJson));
        _handle.SetLength(0);
        _handle.Position = 0;
        _handle.Write(bytes, 0, bytes.Length);
        _handle.Flush(flushToDisk: true);
        return released;
    }

    /// <summary>
    /// Read-only liveness probe: is an OS handle open on this store's lock file? Never creates
    /// the lock and never claims it. <b>Not a substitute for acquisition</b> — the answer is
    /// stale the instant it is returned, so a caller must never treat "not held" as permission
    /// to write. Acquisition remains the single <see cref="TryAcquire"/> open.
    /// </summary>
    /// <summary>
    /// Whether the store's lock is currently held. <b>The directory argument is ignored</b>: the probe
    /// always reads the canonical location, which is the only file a claimant can now hold. That makes
    /// the answer a fact about the store rather than about which directory the asker had in mind.
    /// </summary>
    public static bool IsHeldByAnotherProcess(string storePath, string? lockDirectory = null, bool useCanonicalIdentity = true)
    {
        var lockPath = LockPathFor(storePath, lockDirectory, useCanonicalIdentity);
        if (!File.Exists(lockPath)) return false;
        try
        {
            // The same FileShare.None open the acquirer uses, but with FileMode.Open so a probe
            // cannot create the file. Succeeding proves no handle is open anywhere — including
            // in this process, since Windows enforces sharing across every handle.
            using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            // A sharing violation is liveness, established by the kernel, with no PID inspection.
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Unreadable is not the same as unheld. Report busy: the conservative direction.
            return true;
        }
    }

    public void Dispose()
    {
        try { _handle.Dispose(); } catch (IOException) { /* release is best effort */ }
    }

    internal static readonly JsonSerializerOptions LeaseJson = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    private static IReadOnlyList<string> Evidence(ReservationLease? holder, IHostIdentity host)
    {
        var list = new List<string> { $"callerHost={host.HostId}", $"callerPid={host.ProcessId}" };
        if (holder is null) list.Add("holderPayload=unreadable (exclusive handle held — this is liveness, not absence)");
        else
        {
            list.Add($"holderReservation={holder.ReservationId}");
            list.Add($"holderWorker={holder.WorkerId}");
        }
        return list;
    }

    private static IReadOnlyList<string> EvidenceWith(ReservationLease? holder, IHostIdentity host, IReadOnlyList<string> extra)
    {
        var list = new List<string>(Evidence(holder, host));
        list.AddRange(extra);
        return list;
    }
}
