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
            UnboundSheets = read.UnboundSheets,
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
            ? new DevelopmentControlReservation(result.Lock, storePath)
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
    private readonly string _storePath;

    internal DevelopmentControlReservation(AtomicWriterLock held, string storePath)
    {
        _lock = held;
        _storePath = storePath;
        Lease = new ReservationLeaseView(held.Lease);
    }

    /// <summary>
    /// The WORKBOOK this reservation covers.
    ///
    /// <para><b>Corrected in TASK 9.</b> This returned <c>_lock.LockPath</c>, which is the LOCK
    /// FILE (<c>&lt;hash&gt;.lock</c> inside the lock directory) and not the store at all. A caller
    /// that trusted it would have read, hashed or reported the lock file as if it were the
    /// governance workbook. The lock knows the store only because it was handed one, so the
    /// reservation now carries that value explicitly rather than inferring it from the wrong
    /// property.</para>
    /// </summary>
    public string StorePath => _storePath;

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

    /// <summary>
    /// Writes one cell. Implemented in TASK 9.
    ///
    /// <para><b>Refuses rather than throws, and refuses in five distinct cases</b> — no lease held,
    /// no reservation held, unknown logical sheet, unknown logical column, and a row the sheet does
    /// not carry. Each refusal NAMES the condition, because the caller's remedy differs: re-read the
    /// workbook, fix the column name, or pick a row that exists. A single generic failure would
    /// leave a caller unable to tell "the workbook moved" from "I spelled the column wrong".</para>
    ///
    /// <para><b>The read happens UNDER the held lock.</b> Resolving the logical column against a
    /// cached read would let the workbook change between resolution and write, so the write would
    /// land at a position that no longer means what the caller intended. Re-reading costs one
    /// decode and removes that window entirely.</para>
    /// </summary>
    internal DevelopmentControlWriteResult Write(DevelopmentControlCellWrite write)
    {
        if (!Held)
            return new(false, "Refused: this reservation has already been released, so it no longer "
                            + "excludes another writer. Acquire a new one.", null, null);

        if (_lock.Lease.Expiry <= DateTimeOffset.UtcNow)
            return new(false,
                $"Refused: the lease expired at {_lock.Lease.Expiry:O}. Writing under an expired lease "
                + "means another process may legitimately have reclaimed the lock, so this write could "
                + "race a legitimate writer. Heartbeat or re-acquire first.", null, null);

        var read = WorkbookCompatibilityReader.Read(_storePath);

        var sheet = read.Sheets.FirstOrDefault(s =>
            string.Equals(s.LogicalName, write.LogicalSheet, StringComparison.Ordinal));

        if (sheet is null || sheet.Presence == SheetPresence.DeclaredButMissing)
            return new(false,
                $"Refused: no readable sheet is bound to logical name '{write.LogicalSheet}'. "
                + $"Available: {string.Join(", ", read.Sheets.Select(s => s.LogicalName))}.", null, null);

        var column = sheet.Columns.FirstOrDefault(c =>
            string.Equals(c.LogicalName, write.LogicalColumn, StringComparison.Ordinal));

        if (column is null)
            return new(false,
                $"Refused: '{write.LogicalSheet}' has no logical column '{write.LogicalColumn}'. "
                + $"Available: {string.Join(", ", sheet.Columns.Select(c => c.LogicalName))}.", null, null);

        if (!column.Found || column.PhysicalColumn is null)
            return new(false,
                $"Refused: logical column '{write.LogicalColumn}' is not present in this workbook's "
                + $"form ({read.Form}) — the binding declares no header for it here. Writing anyway "
                + "would mean inventing a column, so the target is reported instead.", null, null);

        if (sheet.PhysicalName is null)
            return new(false, $"Refused: '{write.LogicalSheet}' resolved to no physical sheet.", null, null);

        var previous = sheet.Records
            .FirstOrDefault(r => r.Row == write.Row)?.Get(write.LogicalColumn);

        try
        {
            DevelopmentControlCellWriter.WriteCell(
                _storePath, sheet.PhysicalName, write.Row, column.PhysicalColumn, write.Value);
        }
        catch (WorkbookDecodeException ex)
        {
            return new(false, "Refused: " + ex.Message, previous, null);
        }

        return new(true,
            $"Wrote '{write.Value}' to {sheet.PhysicalName}!{column.PhysicalColumn}{write.Row} "
            + $"(logical {write.LogicalSheet}.{write.LogicalColumn}).",
            previous, write.Value);
    }

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
    /// <summary>
    /// The logical names this lookup queries. These are the names the reader's column bindings
    /// RESOLVE TO on every form, not the physical header text - so <c>Dependencies.SourceId</c> is
    /// this name on legacy (<c>From Node</c>), Foundation/Products (<c>SourceId</c>) and V3
    /// (<c>FromWorkId</c>) alike.
    ///
    /// <para><b>Corrected in TASK 5 after the read proof caught the defect.</b> This class
    /// originally queried <c>WorkItemId</c>, <c>DependsOnWorkItemId</c>, <c>DependsOnId</c>,
    /// <c>Kind</c> and <c>DependencyKind</c>. <i>None of those logical names exists in any
    /// binding</i>, so <see cref="DependenciesOf"/>, <see cref="DependentsOf"/> and
    /// <see cref="LineageOf"/> all returned empty for every input, on every form, silently. The
    /// names below are the ones the binding table actually declares
    /// (<c>WorkbookCompatibilityReader.Columns</c>, the <c>Dependencies</c> rows). Keeping them in
    /// one place, named, is what makes the next such mismatch a compile-time-visible edit rather
    /// than five independent string literals.</para>
    /// </summary>
    private static class Columns
    {
        internal const string Sheet = "Dependencies";
        internal const string Source = "SourceId";
        internal const string Target = "TargetId";
        internal const string RelationType = "RelationType";
        internal const string Status = "Status";
    }

    private readonly DevelopmentControlReadResult _read;

    public DevelopmentControlLookup(DevelopmentControlReadResult read) => _read = read;

    /// <summary>
    /// True when no logical sheet projects Git lineage, so lineage lookups for this workbook are
    /// unavailable rather than empty.
    ///
    /// <para>The V3 candidate is in this state for <c>13_GitLineage</c>: the sheet is physically
    /// present with real headers and rows, and the reader NAMES it in
    /// <see cref="DevelopmentControlReadResult.UnboundSheets"/>, but no column binding decodes it.
    /// "Not readable here" and "this record has no history" are different answers and only the
    /// second is safe to act on, which is why this is a property on the contract and not a
    /// convention.</para>
    ///
    /// <para>Expressed as "the logical sheet is absent" deliberately: that is the honest
    /// mechanism. It stays correct if a GitLineage binding is added later - the property flips to
    /// false without an edit here - which is the behaviour a caller wants.</para>
    /// </summary>
    public bool LineageUnavailable => _read.Sheet("GitLineage") is not { Present: true };

    public IReadOnlyList<DevelopmentControlDependency> DependenciesOf(string workItemId) =>
        EdgesWhere(r => string.Equals(r.Get(Columns.Source), workItemId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<DevelopmentControlDependency> DependentsOf(string workItemId) =>
        EdgesWhere(r => string.Equals(r.Get(Columns.Target), workItemId, StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<DevelopmentControlDependency> EdgesWhere(
        Func<DevelopmentControlRecord, bool> predicate) =>
        _read.Records
            .Where(r => r.LogicalSheet == Columns.Sheet && predicate(r))
            .Select(r => new DevelopmentControlDependency(
                r.Get(Columns.Source) ?? "",
                r.Get(Columns.Target) ?? "",
                r.Get(Columns.RelationType) ?? "",
                r.Get(Columns.Status) ?? ""))
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
