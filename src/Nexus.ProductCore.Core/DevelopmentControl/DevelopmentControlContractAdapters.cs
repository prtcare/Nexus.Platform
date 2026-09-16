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
            // W8D-R5 TASK 1. Was `s.Presence != SheetPresence.DeclaredButMissing`, which returned
            // TRUE for AbsentInForm - a logical sheet the form has no counterpart for, carrying a
            // null PhysicalName and no header row - so the contract reported "present" for a sheet
            // the form declares does not exist. See the corrected doc on
            // DevelopmentControlSheetRead.Present for why this survived until now and what it
            // would have answered wrongly once a GitLineage binding existed.
            //
            // Unreadable stays TRUE: the sheet is in the container and was located, it simply
            // could not be parsed. "Found but unreadable" is not "not found", and a caller
            // deciding whether the data exists wants the first answer.
            Present: s.Presence is SheetPresence.Bound or SheetPresence.Unreadable,
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
            LastRow: s.LastRow)
        {
            // Every bound column that did not resolve, required or not. Found=false has two
            // causes - the form carries no such column, or the form names a header the sheet does
            // not have - and both mean the same thing to a caller: no value is readable here.
            UnprojectedColumns = s.Columns
                .Where(c => !c.Found)
                .Select(c => c.LogicalName)
                .ToArray(),
        };
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
                    ToEngineKind(li.Kind),
                    RepositoryId: "",
                    Path: li.Target,
                    Access: ToEngineAccess(li.AccessMode));
                var b = new ScopeItem(
                    ToEngineKind(ri.Kind),
                    RepositoryId: "",
                    Path: ri.Target,
                    Access: ToEngineAccess(ri.AccessMode));

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

    /// <summary>
    /// Maps the contract's access mode onto the engine's, EXPLICITLY.
    ///
    /// <para><b>This method replaces a cast that inverted every access mode.</b> The two enums
    /// declare the same two members in opposite numeric order —
    /// <c>ChangeScopeAccessMode.Write = 0, Read = 1</c> against
    /// <c>AccessMode.Read = 0, Write = 1</c> — so <c>(AccessMode)item.AccessMode</c> read every
    /// declared WRITE as a read and every declared READ as a write. The failure was not symmetric
    /// in consequence. It ran in the unsafe direction: two lanes both declaring
    /// <c>ExactFile</c>/<c>Write</c> over the same file were evaluated read-against-read, found
    /// <c>Compatible</c>, and dropped from the conflict list — the collision the policy exists to
    /// refuse was the one it silently permitted.</para>
    ///
    /// <para><b>Measured, not reasoned.</b> W8D-R5 TASK 8's negative control was written to show
    /// the cross-host comparison could fail; instead it failed on this. On the fixed probe below,
    /// the two lanes' identical <c>ExactFile</c>/<c>Write</c> on <c>src/shared/file.cs</c>
    /// produced <i>no conflict row</i>, while their identical <c>DirectorySubtree</c>/<c>Read</c> on
    /// <c>src/read-only/</c> was reported as <c>WriteWriteCollision</c> — both inverted, in
    /// opposite directions. Recorded in <c>W8DR5_CHANGESCOPE_ACCEPTANCE_TESTS.md</c>.</para>
    ///
    /// <para><b>Why a map and not a corrected cast.</b> A cast would restore today's correct
    /// behaviour and keep the agreement an unchecked coincidence. The two enums are independent
    /// declarations in two namespaces; either may be reordered or extended without the other
    /// noticing, and the compiler accepts any cast between them. The switch below throws on an
    /// unmapped member instead of defaulting, and <c>CrossHostInterpretationTests</c> walks every
    /// member of both enums so the map's totality is a checked property rather than a claim. C#
    /// gives no compile-time exhaustiveness for enum switches, so a test is the available mechanism
    /// — and it is the one this defect proved was missing.</para>
    /// </summary>
    internal static AccessMode ToEngineAccess(ChangeScopeAccessMode access) => access switch
    {
        ChangeScopeAccessMode.Read => AccessMode.Read,
        ChangeScopeAccessMode.Write => AccessMode.Write,
        _ => throw new ArgumentOutOfRangeException(
            nameof(access), access,
            "Unmapped ChangeScopeAccessMode. Defaulting here would silently pick an access mode, "
            + "and access mode is the input that decides whether two lanes collide."),
    };

    /// <summary>
    /// Maps the contract's item kind onto the engine's, EXPLICITLY, for the same reason
    /// <see cref="ToEngineAccess"/> is a map.
    ///
    /// <para>The kinds happen to agree member-for-member today (both declare
    /// <c>ExactFile = 0</c>, <c>DirectorySubtree = 1</c>, <c>Glob = 2</c>,
    /// <c>ProjectResource = 3</c>, <c>PublicContract = 4</c>, <c>DatabaseMigration = 5</c>,
    /// <c>ControlStore = 6</c>), so the cast this replaces was correct for kinds. That is precisely
    /// what made the cast dangerous: one of the two casts was right, which is why the wrong one
    /// survived review. Kind selects which collision RULES apply, so a silent reroute reasons about
    /// the wrong resource entirely.</para>
    /// </summary>
    internal static Nexus.DevelopmentControl.Safety.ScopeKind ToEngineKind(ChangeScopeItemKind kind) => kind switch
    {
        ChangeScopeItemKind.ExactFile => Nexus.DevelopmentControl.Safety.ScopeKind.ExactFile,
        ChangeScopeItemKind.DirectorySubtree => Nexus.DevelopmentControl.Safety.ScopeKind.DirectorySubtree,
        ChangeScopeItemKind.Glob => Nexus.DevelopmentControl.Safety.ScopeKind.Glob,
        ChangeScopeItemKind.ProjectResource => Nexus.DevelopmentControl.Safety.ScopeKind.ProjectResource,
        ChangeScopeItemKind.PublicContract => Nexus.DevelopmentControl.Safety.ScopeKind.PublicContract,
        ChangeScopeItemKind.DatabaseMigration => Nexus.DevelopmentControl.Safety.ScopeKind.DatabaseMigration,
        ChangeScopeItemKind.ControlStore => Nexus.DevelopmentControl.Safety.ScopeKind.ControlStore,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind,
            "Unmapped ChangeScopeItemKind. Kind decides which collision rules apply, so a default "
            + "here would reason about a different resource than the one declared."),
    };
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

        // W8D-R5 TASK 1. The logical names of the three newly bound surfaces, declared here for
        // the same reason the four above are: one place, so a binding rename is a compile-visible
        // edit rather than a string that quietly stops matching.
        internal static class Lineage
        {
            internal const string Sheet = "GitLineage";
            internal const string LineageId = "LineageId";
            internal const string WorkItemId = "WorkId";
            internal const string ChangeId = "ChangeId";
            internal const string ReservationId = "ReservationId";
            internal const string RepositoryId = "RepositoryId";
            internal const string Branch = "Branch";
            internal const string WorktreePath = "WorktreePath";
            internal const string BaseSha = "BaseSHA";
            internal const string CommitSha = "CommitSha";
            internal const string PullRequest = "PullRequest";
            internal const string IntegrationSha = "IntegrationSha";
            internal const string EffectiveFrom = "EffectiveFrom";
            internal const string IsCurrent = "IsCurrent";
            internal const string Note = "Notes";
        }

        internal static class Scopes
        {
            internal const string Sheet = "ChangeScopes";
            internal const string ScopeRecordId = "ScopeRecordId";
            internal const string WorkItemId = "WorkId";
            internal const string ScopeClass = "ScopeClass";
            internal const string ItemType = "ItemType";
            internal const string Target = "Target";
            internal const string Access = "Access";
            internal const string ReviewState = "ReviewState";
            internal const string IsCurrent = "IsCurrent";
            internal const string Note = "Notes";
        }

        internal static class Requests
        {
            internal const string Sheet = "ChangeRequests";
            internal const string RequestId = "RequestId";
            internal const string RequestType = "RequestType";
            internal const string Destination = "Destination";
            internal const string RequestingWorkId = "RequestingWorkId";
            internal const string RequestingHead = "RequestingHead";
            internal const string CapabilityRequested = "CapabilityRequested";
            internal const string Purpose = "Purpose";
            internal const string ContextRefs = "ContextRefs";
            internal const string DataClassification = "DataClassification";
            internal const string ExecutionPolicy = "ExecutionPolicy";
            internal const string ToolPermissionProfile = "ToolPermissionProfile";
            internal const string Status = "Status";
            internal const string PlatformChangeRequestId = "PlatformChangeRequestId";
            internal const string HandbackWorkId = "HandbackWorkId";
            internal const string LegacyStatusText = "LegacyStatusText";
            internal const string IsCurrent = "IsCurrent";
            internal const string Note = "Notes";
        }

        internal static class Acceptance
        {
            internal const string Sheet = "WorkGraph";
            internal const string WorkItemId = "WorkId";
            internal const string Criteria = "AcceptanceCriteria";
            internal const string Evidence = "EvidenceRequired";
            internal const string Readiness = "ReadinessState";
        }
    }

    private readonly DevelopmentControlReadResult _read;

    public DevelopmentControlLookup(DevelopmentControlReadResult read) => _read = read;

    /// <summary>
    /// True when this workbook has no readable Git-lineage surface.
    ///
    /// <para><b>W8D-R5 TASK 1 flipped this for V3, and the flip is not the whole story.</b> R4
    /// measured it true because <c>GitLineage</c> had no binding at all, so
    /// <c>Sheet("GitLineage")</c> returned null. With the binding added it is false for the V3
    /// candidate and stays true on the three frozen forms, where the logical sheet is absent by
    /// construction - the behaviour the original comment predicted.</para>
    ///
    /// <para><b>But false does not mean there is lineage to read.</b> The V3 candidate's
    /// <c>13_GitLineage</c> carries 26 headers on row 4 and <b>zero data rows</b>, so every
    /// <see cref="LineageOf"/> result is empty for a reason that has nothing to do with the record
    /// asked about. Callers must therefore check
    /// <see cref="DevelopmentControlReadResult.Coverage"/> for
    /// <c>CarriesData</c> before reading an empty result as "this record has no history". This
    /// property answers "is the surface readable"; that one answers "does it hold anything".</para>
    /// </summary>
    public bool LineageUnavailable => SurfaceUnavailable(Columns.Lineage.Sheet);

    /// <summary>True when this workbook has no readable change-scope surface.</summary>
    public bool ChangeScopesUnavailable => SurfaceUnavailable(Columns.Scopes.Sheet);

    /// <summary>True when this workbook has no readable Core Change Request surface.</summary>
    public bool ChangeRequestsUnavailable => SurfaceUnavailable(Columns.Requests.Sheet);

    /// <summary>
    /// True when the acceptance-state columns cannot be read. Unlike the three above this is not a
    /// whole sheet - acceptance state is three columns of the work-item sheet - so a form that has
    /// the sheet and not the columns is unavailable too. That is the frozen forms' state: they all
    /// have WorkGraph and none of them has these columns.
    /// </summary>
    public bool AcceptanceUnavailable =>
        SurfaceUnavailable(Columns.Acceptance.Sheet)
        || _read.Sheet(Columns.Acceptance.Sheet) is { } w
           && new[]
              {
                  Columns.Acceptance.Criteria,
                  Columns.Acceptance.Evidence,
                  Columns.Acceptance.Readiness,
              }.Any(c => w.UnprojectedColumns.Contains(c, StringComparer.Ordinal));

    /// <summary>
    /// "The form has no physical counterpart for this logical sheet, or it has one that could not
    /// be located." Expressed against <see cref="DevelopmentControlSheetRead.Present"/>, which
    /// W8D-R5 TASK 1 corrected so that it means exactly that.
    /// </summary>
    private bool SurfaceUnavailable(string logicalSheet) =>
        _read.Sheet(logicalSheet) is not { Present: true };

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
    /// Git lineage rows for a control record.
    ///
    /// <para><b>Corrected by W8D-R5 TASK 1 along with the binding.</b> The previous implementation
    /// read <c>RecordKey</c>, <c>Commit</c> and <c>BlobSha256</c>. None of the three is a logical
    /// name any binding declares - the same class of defect the W8D-R4 TASK 5 note on
    /// <c>Columns</c> records - so this returned empty for every input on every form, and it would
    /// have gone on doing so after the binding was added, silently, because an empty lineage
    /// result is a legal answer. Compiling against declared constants is what makes that a build
    /// failure next time rather than a silence.</para>
    ///
    /// <para><paramref name="logicalSheet"/> selects which foreign key on the lineage row answers
    /// for the record. A sheet this lookup does not know is matched against all four keys, which
    /// is a deliberate SUPERSET: a caller that passes an unrecognised or future logical sheet name
    /// gets its lineage when a key matches rather than silently nothing. Pass a recognised name
    /// when precision matters.</para>
    /// </summary>
    public IReadOnlyList<DevelopmentControlGitLineage> LineageOf(string logicalSheet, string recordKey)
    {
        if (LineageUnavailable) return [];

        var key = ForeignKeyFor(logicalSheet);
        var keys = key is null
            ? new[]
              {
                  Columns.Lineage.LineageId, Columns.Lineage.WorkItemId,
                  Columns.Lineage.ChangeId, Columns.Lineage.ReservationId,
              }
            : new[] { key };

        return _read.Records
            .Where(r => r.LogicalSheet == Columns.Lineage.Sheet
                     && keys.Any(k => string.Equals(r.Get(k), recordKey, StringComparison.OrdinalIgnoreCase)))
            .Select(r => new DevelopmentControlGitLineage(
                r.Get(Columns.Lineage.LineageId) ?? "",
                r.Get(Columns.Lineage.WorkItemId) ?? "",
                r.Get(Columns.Lineage.ChangeId) ?? "",
                r.Get(Columns.Lineage.ReservationId) ?? "",
                r.Get(Columns.Lineage.RepositoryId) ?? "",
                r.Get(Columns.Lineage.Branch) ?? "",
                r.Get(Columns.Lineage.WorktreePath) ?? "",
                r.Get(Columns.Lineage.BaseSha) ?? "",
                r.Get(Columns.Lineage.CommitSha) ?? "",
                r.Get(Columns.Lineage.PullRequest) ?? "",
                r.Get(Columns.Lineage.IntegrationSha) ?? "",
                ParseTimestamp(r.Get(Columns.Lineage.EffectiveFrom)),
                r.Get(Columns.Lineage.IsCurrent) ?? "",
                r.Get(Columns.Lineage.Note) ?? ""))
            .ToArray();
    }

    /// <summary>
    /// The lineage foreign key that answers for a given logical sheet, or null when this lookup
    /// does not recognise the sheet and must fall back to matching all of them.
    /// </summary>
    private static string? ForeignKeyFor(string logicalSheet) => logicalSheet switch
    {
        "WorkGraph" => Columns.Lineage.WorkItemId,
        "Changes" => Columns.Lineage.ChangeId,
        Columns.Lineage.Sheet => Columns.Lineage.LineageId,
        _ => null,
    };

    public IReadOnlyList<DevelopmentControlScopeRow> ScopesOf(string workItemId) =>
        ChangeScopesUnavailable
            ? []
            : _read.Records
                .Where(r => r.LogicalSheet == Columns.Scopes.Sheet
                         && string.Equals(r.Get(Columns.Scopes.WorkItemId), workItemId, StringComparison.OrdinalIgnoreCase))
                .Select(r => new DevelopmentControlScopeRow(
                    r.Get(Columns.Scopes.ScopeRecordId) ?? "",
                    r.Get(Columns.Scopes.WorkItemId) ?? "",
                    r.Get(Columns.Scopes.ScopeClass) ?? "",
                    r.Get(Columns.Scopes.ItemType) ?? "",
                    r.Get(Columns.Scopes.Target) ?? "",
                    r.Get(Columns.Scopes.Access) ?? "",
                    r.Get(Columns.Scopes.ReviewState) ?? "",
                    r.Get(Columns.Scopes.IsCurrent) ?? "",
                    r.Get(Columns.Scopes.Note) ?? ""))
                .ToArray();

    public IReadOnlyList<DevelopmentControlChangeRequest> ChangeRequestsOf(string workItemId) =>
        ChangeRequestsUnavailable
            ? []
            : _read.Records
                .Where(r => r.LogicalSheet == Columns.Requests.Sheet
                         && string.Equals(r.Get(Columns.Requests.RequestingWorkId), workItemId, StringComparison.OrdinalIgnoreCase))
                .Select(r => new DevelopmentControlChangeRequest(
                    r.Get(Columns.Requests.RequestId) ?? "",
                    r.Get(Columns.Requests.RequestType) ?? "",
                    r.Get(Columns.Requests.Destination) ?? "",
                    r.Get(Columns.Requests.RequestingWorkId) ?? "",
                    r.Get(Columns.Requests.RequestingHead) ?? "",
                    r.Get(Columns.Requests.CapabilityRequested) ?? "",
                    r.Get(Columns.Requests.Purpose) ?? "",
                    r.Get(Columns.Requests.ContextRefs) ?? "",
                    r.Get(Columns.Requests.DataClassification) ?? "",
                    r.Get(Columns.Requests.ExecutionPolicy) ?? "",
                    r.Get(Columns.Requests.ToolPermissionProfile) ?? "",
                    r.Get(Columns.Requests.Status) ?? "",
                    r.Get(Columns.Requests.PlatformChangeRequestId) ?? "",
                    r.Get(Columns.Requests.HandbackWorkId) ?? "",
                    r.Get(Columns.Requests.LegacyStatusText) ?? "",
                    r.Get(Columns.Requests.IsCurrent) ?? "",
                    r.Get(Columns.Requests.Note) ?? ""))
                .ToArray();

    /// <summary>
    /// Null when the workbook declares no acceptance state for the work item, which is a finding:
    /// a work item with no declared criteria cannot be accepted, only asserted complete.
    /// </summary>
    public DevelopmentControlAcceptance? AcceptanceOf(string workItemId)
    {
        if (AcceptanceUnavailable) return null;

        var row = _read.Records.FirstOrDefault(
            r => r.LogicalSheet == Columns.Acceptance.Sheet
              && string.Equals(r.Get(Columns.Acceptance.WorkItemId), workItemId, StringComparison.OrdinalIgnoreCase));

        return row is null
            ? null
            : new DevelopmentControlAcceptance(
                row.Get(Columns.Acceptance.WorkItemId) ?? "",
                row.Get(Columns.Acceptance.Criteria) ?? "",
                row.Get(Columns.Acceptance.Evidence) ?? "",
                row.Get(Columns.Acceptance.Readiness) ?? "");
    }

    /// <summary>
    /// Parses an ISO-8601 timestamp, returning null when it is blank or unparseable.
    ///
    /// <para>Null is "the workbook did not state a time", never a sentinel. Returning
    /// <see cref="DateTimeOffset.MinValue"/> or <c>default</c> would put a real-looking instant on
    /// a record whose time nothing recorded — and for a lineage row that instant is a claim about
    /// when work happened.</para>
    /// </summary>
    private static DateTimeOffset? ParseTimestamp(string? text) =>
        DateTimeOffset.TryParse(
            text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var value)
            ? value
            : null;
}
