using Nexus.ProductCore.Contracts.DevelopmentControl;
using Nexus.DevelopmentControl.Safety;

namespace Nexus.ProductCore.Core.DevelopmentControl;

/// <summary>
/// W8D-R4 TASK 3. Adapts the canonical DevelopmentControl mechanisms - moved here from Forge in
/// TASK 2 - onto the shared contract surface in <c>Nexus.ProductCore.Contracts</c>.
///
/// <para><b>Why an adapter and not a rewrite.</b> The moved lineage is byte-identical to what
/// Forge and Developer run today, and its lock-identity file is pinned by SHA-256 in both hosts.
/// Re-spelling the implementation to implement the interfaces directly would change those bytes,
/// invalidate the pin, and destroy the cross-host identity proof that W8D-R3 established over 22
/// vector inputs. Adapting around it keeps the proven mechanism exactly as proven while still
/// giving callers a stable, OOXML-free surface. Nothing here reimplements a mechanism; every
/// member delegates.</para>
///
/// <para><b>The one real translation</b> is the lock's return shape: the implementation's
/// <c>AcquireResult</c> carries an <c>AtomicWriterLock?</c>, which is an implementation type and
/// must not cross the contract. The adapter hands back the lock as
/// <see cref="IDevelopmentControlReservation"/> instead.</para>
/// </summary>
internal static class FormMap
{
    internal static DevelopmentControlForm ToContract(WorkbookForm form) => form switch
    {
        WorkbookForm.LegacyL2 => DevelopmentControlForm.Legacy,
        WorkbookForm.V2Foundation => DevelopmentControlForm.Foundation,
        WorkbookForm.V2Products => DevelopmentControlForm.Products,
        WorkbookForm.V3 => DevelopmentControlForm.V3,
        _ => DevelopmentControlForm.Unknown,
    };

    internal static WorkbookForm ToImplementation(DevelopmentControlForm form) => form switch
    {
        DevelopmentControlForm.Legacy => WorkbookForm.LegacyL2,
        DevelopmentControlForm.Foundation => WorkbookForm.V2Foundation,
        DevelopmentControlForm.Products => WorkbookForm.V2Products,
        DevelopmentControlForm.V3 => WorkbookForm.V3,
        _ => WorkbookForm.Unknown,
    };

    /// <summary>
    /// Reads authority from the workbook's own control block. Anything unrecognised is
    /// <see cref="DevelopmentControlAuthority.Unknown"/> and not a guess - an unreadable marker
    /// must never be read as "authoritative".
    /// </summary>
    internal static DevelopmentControlAuthority AuthorityOf(WorkbookReadResult read)
    {
        var marker = WorkbookCompatibilityReader.AuthorityMarker(read);
        return marker switch
        {
            WorkbookCompatibilityReader.AuthorityMarkerAuthoritative => DevelopmentControlAuthority.Authoritative,
            WorkbookCompatibilityReader.AuthorityMarkerCandidate => DevelopmentControlAuthority.Candidate,
            WorkbookCompatibilityReader.LegacyReadOnlyDesignation => DevelopmentControlAuthority.LegacyReadOnly,
            _ => DevelopmentControlAuthority.Unknown,
        };
    }
}

/// <summary>The canonical reader, exposed through the shared contract.</summary>
public sealed class DevelopmentControlReader : IDevelopmentControlReader
{
    public DevelopmentControlReadResult Read(string workbookPath, DevelopmentControlForm? formHint = null)
    {
        var hint = formHint is null ? (WorkbookForm?)null : FormMap.ToImplementation(formHint.Value);
        var read = WorkbookCompatibilityReader.Read(workbookPath, hint);

        return new DevelopmentControlReadResult(
            Path: read.SourcePath,
            Form: FormMap.ToContract(read.Form),
            Sha256: read.SourceSha256,
            Authority: FormMap.AuthorityOf(read),
            SchemaId: WorkbookCompatibilityReader.ControlItems(read)
                          .TryGetValue("SchemaId", out var sid) ? sid : "",
            SchemaVersion: WorkbookCompatibilityReader.ControlItems(read)
                          .TryGetValue("SchemaVersion", out var sv) ? sv : "",
            Sheets: read.Sheets.Select(ToContract).ToArray(),
            Diagnostics: read.Diagnostics)
        {
            Records = read.Records
                .Select(r => new DevelopmentControlRecord(r.LogicalSheet, r.Row, r.Values))
                .ToArray(),
        };
    }

    public string Sha256Of(string path) => WorkbookCompatibilityReader.Sha256Of(path);

    private static DevelopmentControlSheetRead ToContract(SheetRead s) =>
        new(s.LogicalName,
            Present: s.Presence != SheetPresence.DeclaredButMissing,
            PhysicalName: s.PhysicalName,
            HeaderRow: s.HeaderRow,
            HeaderRowLocated: s.HeaderRowLocated,
            MissingRequiredColumns: s.Columns
                .Where(c => c.Required && !c.Found)
                .Select(c => c.LogicalName)
                .ToArray(),
            Records: s.Records
                .Select(r => new DevelopmentControlRecord(r.LogicalSheet, r.Row, r.Values))
                .ToArray(),
            DecodeFailures: s.DecodeFailures,
            LastRow: s.LastRow);
}

/// <summary>
/// The canonical write authorization, exposed through the shared contract.
///
/// <para>Authorization is <b>advisory and pure</b>: it reads the target and answers, and it
/// acquires nothing. A caller that acts on <see cref="Allowed"/> without holding a reservation
/// will still be refused by <see cref="DevelopmentControlLockService"/>.</para>
/// </summary>
public sealed class DevelopmentControlWriterAuthorizer : IDevelopmentControlWriter
{
    public DevelopmentControlWriteAuthorisation AuthoriseWrite(
        string workbookPath, DevelopmentControlForm intendedForm)
    {
        var read = WorkbookCompatibilityReader.Read(workbookPath);
        var auth = WorkbookCompatibilityReader.AuthorizeWrite(
            read, FormMap.ToImplementation(intendedForm));

        return new DevelopmentControlWriteAuthorisation(
            Verdict: auth.Verdict switch
            {
                WorkbookCompatibilityReader.WriteVerdict.Permitted => DevelopmentControlWriteVerdict.Allowed,
                WorkbookCompatibilityReader.WriteVerdict.RefusedSchemaUnrecognised => DevelopmentControlWriteVerdict.RefusedUnreadable,
                WorkbookCompatibilityReader.WriteVerdict.RefusedSchemaMismatch => DevelopmentControlWriteVerdict.RefusedFormMismatch,
                WorkbookCompatibilityReader.WriteVerdict.RefusedSchemaVersionMismatch => DevelopmentControlWriteVerdict.RefusedSchema,
                WorkbookCompatibilityReader.WriteVerdict.RefusedLegacyReadOnly => DevelopmentControlWriteVerdict.RefusedLegacyReadOnly,
                WorkbookCompatibilityReader.WriteVerdict.RefusedAuthorityUnavailable => DevelopmentControlWriteVerdict.RefusedPath,
                WorkbookCompatibilityReader.WriteVerdict.RefusedAuthorityMarkerUnreadable => DevelopmentControlWriteVerdict.RefusedCandidate,
                _ => DevelopmentControlWriteVerdict.Unknown,
            },
            Allowed: auth.Allowed,
            Form: FormMap.ToContract(auth.Form),
            TargetPath: auth.TargetPath,
            SchemaId: auth.SchemaId,
            SchemaVersion: auth.SchemaVersion,
            Authority: FormMap.AuthorityOf(read),
            Reason: auth.Reason);
    }

    /// <summary>
    /// TASK 9's write path lands here. It refuses rather than throws, and it refuses when the
    /// reservation is not held - the lock is the enforcement, not this method.
    /// </summary>
    public DevelopmentControlWriteResult Write(
        IDevelopmentControlReservation reservation, DevelopmentControlCellWrite write)
    {
        if (reservation is not DevelopmentControlReservation held)
            return new DevelopmentControlWriteResult(
                false,
                "The reservation was not issued by this component. A reservation from another "
                + "implementation cannot be verified as holding the canonical lock, and accepting "
                + "it would defeat the single-writer property.", null, null);

        return held.Write(write);
    }
}

/// <summary>The canonical writer lock, exposed through the shared contract.</summary>
public sealed class DevelopmentControlLockService : IDevelopmentControlLockService
{
    public DevelopmentControlLockAttempt TryAcquire(
        string storePath, string lockDirectory, string? owner = null)
    {
        var requested = new ReservationLease
        {
            ReservationId = Guid.NewGuid().ToString("N"),
            WorkerId = owner ?? "",
            LeaseStart = DateTimeOffset.UtcNow,
            LastHeartbeat = DateTimeOffset.UtcNow,
            Expiry = DateTimeOffset.UtcNow + LeasePolicy.DefaultLease,
        };

        var result = AtomicWriterLock.TryAcquire(
            storePath, lockDirectory, requested, MachineHostIdentity.Instance);

        // The reservation is handed back only when the claim is actually held. A caller that
        // received one on a Busy outcome would be able to attempt a write it does not own.
        var reservation = result.Acquired && result.Lock is not null
            ? new DevelopmentControlReservation(result.Lock)
            : null;

        return new DevelopmentControlLockAttempt(
            Outcome: result.Outcome switch
            {
                AcquireOutcome.Acquired => DevelopmentControlLockOutcome.Acquired,
                AcquireOutcome.BusyLive => DevelopmentControlLockOutcome.BusyLive,
                AcquireOutcome.BusyStaleLive => DevelopmentControlLockOutcome.BusyStaleLive,
                AcquireOutcome.Reclaimable => DevelopmentControlLockOutcome.Reclaimable,
                AcquireOutcome.ReclaimBlocked => DevelopmentControlLockOutcome.ReclaimBlocked,
                _ => DevelopmentControlLockOutcome.Unknown,
            },
            Detail: result.Detail,
            Evidence: result.Evidence,
            Holder: result.Holder is null ? null : new ReservationLeaseView(result.Holder),
            Reservation: reservation);
    }

    public bool IsHeldByAnotherProcess(string storePath, string lockDirectory) =>
        AtomicWriterLock.IsHeldByAnotherProcess(storePath, lockDirectory);

    /// <summary>
    /// The canonical lock identity for a path, so a host can assert cross-host agreement without
    /// acquiring anything. Both hosts must derive the same string or they contend on different
    /// kernel objects while each believing it holds the lock.
    /// </summary>
    public string LockIdentityFor(string storePath) =>
        DevelopmentControlStoreIdentity.CanonicalFromWorkbookPath(storePath).ObjectName;
}

/// <summary>A held reservation. The only handle that may perform a write.</summary>
public sealed class DevelopmentControlReservation : IDevelopmentControlReservation, ILeaseHeartbeat
{
    private readonly AtomicWriterLock _lock;

    internal DevelopmentControlReservation(AtomicWriterLock held)
    {
        _lock = held;
        Lease = new ReservationLeaseView(held.Lease);
    }

    public string StorePath => _lock.LockPath;

    public bool Held { get; private set; } = true;

    public IReservationLease Lease { get; private set; }

    public IReservationLease Heartbeat(TimeSpan? extend = null) =>
        Lease = new ReservationLeaseView(_lock.Heartbeat(extend));

    public IReservationLease Release()
    {
        var released = new ReservationLeaseView(_lock.Release());
        Held = false;
        return released;
    }

    internal DevelopmentControlWriteResult Write(DevelopmentControlCellWrite write) =>
        // The cell-write path is implemented in TASK 9, where it is proven against a disposable
        // copy. Until then this refuses explicitly rather than appearing to succeed - a write
        // path that silently no-ops is the defect class this whole component exists to prevent.
        new(false,
            "Refused: the shared contract's write path is not yet wired to the cell codec. "
            + "W8D-R4 TASK 9 carries the proof; see W8DR4_FIXTURE_WRITE_PROOF.md.",
            null, null);

    public void Dispose()
    {
        if (Held) Release();
        _lock.Dispose();
    }
}

/// <summary>
/// Read-only view of a lease. The implementation's <c>ReservationLease</c> is a mutable
/// configuration record with ~25 members; the contract deliberately exposes the six a caller
/// can act on, so adding an implementation field is not a contract change.
/// </summary>
public sealed class ReservationLeaseView : IReservationLease
{
    private readonly ReservationLease _lease;

    public ReservationLeaseView(ReservationLease lease) => _lease = lease;

    public string Owner => _lease.WorkerId;

    public DateTimeOffset AcquiredAt => _lease.LeaseStart;

    public DateTimeOffset ExpiresAt => _lease.Expiry;

    public string HostId => _lease.HostId;

    public bool Expired => DateTimeOffset.UtcNow >= _lease.Expiry;
}

/// <summary>The canonical ChangeScope collision policy, exposed through the shared contract.</summary>
public sealed class DevelopmentControlChangeScopePolicy : IChangeScopePolicy
{
    public ChangeScopeCollisionResult Evaluate(IReadOnlyList<ChangeScopeDeclaration> declared)
    {
        var conflicts = new List<ChangeScopeConflict>();

        for (var i = 0; i < declared.Count; i++)
        for (var j = i + 1; j < declared.Count; j++)
        {
            var left = declared[i];
            var right = declared[j];

            foreach (var li in left.Items)
            foreach (var ri in right.Items)
            {
                var a = new ScopeItem(
                    (Nexus.DevelopmentControl.Safety.ScopeKind)li.Kind,
                    RepositoryId: "",
                    Path: li.Target,
                    Access: (AccessMode)li.AccessMode);
                var b = new ScopeItem(
                    (Nexus.DevelopmentControl.Safety.ScopeKind)ri.Kind,
                    RepositoryId: "",
                    Path: ri.Target,
                    Access: (AccessMode)ri.AccessMode);

                // Fully qualified: the engine and this file's contract record would otherwise
                // be the same identifier in scope.
                var verdict = Nexus.DevelopmentControl.Safety.ChangeScopeCollision.Evaluate(a, b);

                if (verdict.Verdict == CollisionVerdict.Compatible) continue;

                conflicts.Add(new ChangeScopeConflict(
                    left.Lane, right.Lane, li.Target,
                    verdict.Verdict switch
                    {
                        CollisionVerdict.Collision => ChangeScopeCollisionVerdict.WriteWriteCollision,
                        CollisionVerdict.Undetermined => ChangeScopeCollisionVerdict.Indeterminate,
                        _ => ChangeScopeCollisionVerdict.Disjoint,
                    }));
            }
        }

        // Undetermined gates as a collision and must never fold into Compatible - the same
        // fail-closed rule the implementation applies.
        var compatible = conflicts.All(c => c.Verdict == ChangeScopeCollisionVerdict.Disjoint);

        return new ChangeScopeCollisionResult(
            conflicts,
            compatible,
            conflicts.Count == 0
                ? "No declared scope items collide."
                : $"{conflicts.Count} conflict(s) between declared scopes.");
    }

    public bool AreCompatible(IReadOnlyList<ChangeScopeDeclaration> declared) =>
        Evaluate(declared).Compatible;
}

/// <summary>
/// Dependency and lineage lookup over an already-read workbook. Constructed from a
/// <see cref="DevelopmentControlReadResult"/> rather than reading again, so a caller cannot
/// accidentally look up lineage in a different revision from the one it validated.
/// </summary>
public sealed class DevelopmentControlLookup : IDependencyLineageLookup
{
    private readonly DevelopmentControlReadResult _read;

    public DevelopmentControlLookup(DevelopmentControlReadResult read) => _read = read;

    /// <summary>
    /// True when the lineage sheet is absent OR present-but-unbound. The V3 candidate is in the
    /// second state for <c>13_GitLineage</c>; callers must read that as "unavailable", never as
    /// "this record has no history".
    /// </summary>
    public bool LineageUnavailable =>
        _read.Sheet("GitLineage") is not { Present: true }
        || LineageSheetUnbound();

    private bool LineageSheetUnbound() =>
        _read.Diagnostics.Any(d => d.Contains("13_GitLineage", StringComparison.OrdinalIgnoreCase)
                                && d.Contains("UNBOUND", StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<DevelopmentControlDependency> DependenciesOf(string workItemId) =>
        _read.Records
            .Where(r => r.LogicalSheet == "Dependencies"
                     && string.Equals(r.Get("WorkItemId"), workItemId, StringComparison.OrdinalIgnoreCase))
            .Select(r => new DevelopmentControlDependency(
                r.Get("WorkItemId") ?? "",
                r.Get("DependsOnWorkItemId") ?? r.Get("DependsOnId") ?? "",
                r.Get("Kind") ?? r.Get("DependencyKind") ?? "",
                r.Get("Status") ?? ""))
            .ToArray();

    public IReadOnlyList<DevelopmentControlDependency> DependentsOf(string workItemId) =>
        _read.Records
            .Where(r => r.LogicalSheet == "Dependencies"
                     && string.Equals(r.Get("DependsOnWorkItemId") ?? r.Get("DependsOnId"),
                                      workItemId, StringComparison.OrdinalIgnoreCase))
            .Select(r => new DevelopmentControlDependency(
                r.Get("WorkItemId") ?? "",
                r.Get("DependsOnWorkItemId") ?? r.Get("DependsOnId") ?? "",
                r.Get("Kind") ?? r.Get("DependencyKind") ?? "",
                r.Get("Status") ?? ""))
            .ToArray();

    /// <summary>
    /// Empty when the workbook carries no lineage for the record. Because that is
    /// indistinguishable from "no history" at this layer, callers must consult
    /// <see cref="LineageUnavailable"/> first - which is why it is on the contract.
    /// </summary>
    public IReadOnlyList<DevelopmentControlLineageEntry> LineageOf(string logicalSheet, string recordKey) =>
        LineageUnavailable
            ? []
            : _read.Records
                .Where(r => r.LogicalSheet == "GitLineage"
                         && (string.Equals(r.Get("RecordKey"), recordKey, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(r.Get("WorkItemId"), recordKey, StringComparison.OrdinalIgnoreCase)))
                .Select(r => new DevelopmentControlLineageEntry(
                    r.Get("Commit") ?? "",
                    r.Get("BlobSha256") ?? r.Get("Sha256") ?? "",
                    null,
                    r.Get("Note") ?? ""))
                .ToArray();
}
