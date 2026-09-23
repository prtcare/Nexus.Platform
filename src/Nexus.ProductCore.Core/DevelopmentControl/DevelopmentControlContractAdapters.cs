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

    /// <summary>
    /// TASK 2. Appends one record to a sheet that already exists.
    ///
    /// <para>The reservation check is identical to <see cref="Write"/>'s and for the same reason: a
    /// reservation from another implementation cannot be verified as holding the canonical lock, so
    /// accepting it would let a caller perform a governed append while some other writer held the
    /// authority. The refusal is on the TYPE of the reservation, which no caller can forge by
    /// implementing the interface — only this component constructs a
    /// <see cref="DevelopmentControlReservation"/>.</para>
    /// </summary>
    public DevelopmentControlAppendResult Append(
        IDevelopmentControlReservation reservation, DevelopmentControlAppendRecord record)
    {
        if (reservation is not DevelopmentControlReservation held)
            return new DevelopmentControlAppendResult(
                false,
                "The reservation was not issued by this component. A reservation from another "
                + "implementation cannot be verified as holding the canonical lock, and accepting "
                + "it would defeat the single-writer property.", null, null, null, null);

        return held.Append(record);
    }

    /// <summary>
    /// W8D FINAL TASK 3. The authority cutover, exposed through the shared contract.
    ///
    /// <para><b>Why the reservation check comes first and is not negotiable.</b> A cutover that
    /// could run without the lock would be a second writer on the one file whose single-writer
    /// property the whole component exists to provide — and unlike an ordinary write, it would
    /// change what every subsequent reader treats as authoritative. A reservation from another
    /// implementation cannot be verified as holding the canonical lock, because only this component
    /// constructs a <see cref="DevelopmentControlReservation"/>, so the check is on the TYPE.</para>
    ///
    /// <para>A refusal is a RESULT, not an exception. The caller is asking a governed question and
    /// the answer may legitimately be no; throwing would make "already authoritative" — a benign
    /// no-op — indistinguishable from a failure at the call site.</para>
    /// </summary>
    public DevelopmentControlCutoverResult Cutover(
        IDevelopmentControlReservation reservation, DevelopmentControlCutoverRequest request)
    {
        if (reservation is not DevelopmentControlReservation held)
            return new DevelopmentControlCutoverResult(
                Performed: false,
                Verdict: DevelopmentControlCutoverVerdict.RefusedNoReservation,
                Reason: "The reservation was not issued by this component. A reservation from another "
                      + "implementation cannot be verified as holding the canonical lock, and a cutover "
                      + "performed under an unverified lock would promote the authority while some other "
                      + "writer believed it held exclusivity.",
                Path: "", Sha256Before: "", Sha256After: "", StateBefore: "", StateAfter: "",
                Sites: Array.Empty<DevelopmentControlCutoverSiteChange>(),
                UnresolvedDecisionCount: 0, StructuralIntegrityReport: "",
                ChangeId: request?.ChangeId ?? "", Rationale: request?.Rationale ?? "",
                EvidenceRef: request?.Attestation?.EvidenceRef ?? "");

        return held.Cutover(request);
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
                AcquireOutcome.LockDirectoryNotCanonical => DevelopmentControlLockOutcome.LockDirectoryNotCanonical,
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
                            + "excludes another writer. Acquire a new one.", null, null,
                            DevelopmentControlWriteVerdict.RefusedReservation);

        if (_lock.Lease.Expiry <= DateTimeOffset.UtcNow)
            return new(false,
                $"Refused: the lease expired at {_lock.Lease.Expiry:O}. Writing under an expired lease "
                + "means another process may legitimately have reclaimed the lock, so this write could "
                + "race a legitimate writer. Heartbeat or re-acquire first.", null, null,
                DevelopmentControlWriteVerdict.RefusedReservation);

        var read = WorkbookCompatibilityReader.Read(_storePath);

        var sheet = read.Sheets.FirstOrDefault(s =>
            string.Equals(s.LogicalName, write.LogicalSheet, StringComparison.Ordinal));

        if (sheet is null || sheet.Presence == SheetPresence.DeclaredButMissing)
            return new(false,
                $"Refused: no readable sheet is bound to logical name '{write.LogicalSheet}'. "
                + $"Available: {string.Join(", ", read.Sheets.Select(s => s.LogicalName))}.", null, null,
                DevelopmentControlWriteVerdict.RefusedBinding);

        var column = sheet.Columns.FirstOrDefault(c =>
            string.Equals(c.LogicalName, write.LogicalColumn, StringComparison.Ordinal));

        if (column is null)
            return new(false,
                $"Refused: '{write.LogicalSheet}' has no logical column '{write.LogicalColumn}'. "
                + $"Available: {string.Join(", ", sheet.Columns.Select(c => c.LogicalName))}.", null, null,
                DevelopmentControlWriteVerdict.RefusedBinding);

        if (!column.Found || column.PhysicalColumn is null)
            return new(false,
                $"Refused: logical column '{write.LogicalColumn}' is not present in this workbook's "
                + $"form ({read.Form}) — the binding declares no header for it here. Writing anyway "
                + "would mean inventing a column, so the target is reported instead.", null, null,
                DevelopmentControlWriteVerdict.RefusedBinding);

        if (sheet.PhysicalName is null)
            return new(false, $"Refused: '{write.LogicalSheet}' resolved to no physical sheet.", null, null,
                            DevelopmentControlWriteVerdict.RefusedBinding);

        // --- W8D FINAL: the authority sites are refused, in the caller's own vocabulary.
        //
        // The writer refuses these cells PHYSICALLY as well, and that guard is the one that cannot
        // be stepped around. This check exists because the physical refusal can only report
        // "GOVERNED_CELL — 00_Control!B10", which names a spreadsheet address to a caller that
        // never spoke in those terms. Answering the question the caller actually asked — "may I
        // write Control.Value at row 10" — is what makes the refusal actionable rather than
        // merely correct.
        //
        // Only two of the four sites are reachable here. `01_Configuration` and `25_Dashboard` are
        // not bound by the compatibility map, so a logical write cannot address them at all; they
        // are covered by the physical guard and by the cutover, not by this check.
        var authoritySite = DevelopmentControlAuthoritySites.FindLogical(
            write.LogicalSheet, write.Row, write.LogicalColumn);

        if (authoritySite is not null)
            return new(false, "Refused: " + DevelopmentControlAuthoritySites.RefusalReason(authoritySite),
                null, null, DevelopmentControlWriteVerdict.RefusedAuthoritySite);

        // --- W8D PRODUCTION WIRING: the site's IDENTITY cell, refused in the caller's own vocabulary.
        //
        // Same shape and same justification as the check above, for the cell that NAMES the marker
        // rather than the one that carries it. `Control.ControlItem` at row 10 reads `ControlState`,
        // and that is how `ControlItems` finds the marker at all — so an ordinary write there does
        // not move the authority state, it deletes the question. The workbook afterwards reports its
        // authority as unreadable, and `AuthorizeWrite` refuses every subsequent write with
        // `RefusedAuthorityMarkerUnreadable`. Measured in both directions before this guard existed:
        // the write was Allowed, the marker became `<UNREADABLE>`, and the resolver said Unknown.
        var identitySite = DevelopmentControlAuthoritySites.FindLogicalIdentity(
            write.LogicalSheet, write.Row, write.LogicalColumn);

        if (identitySite is not null)
            return new(false, "Refused: " + DevelopmentControlAuthoritySites.IdentityRefusalReason(identitySite),
                null, null, DevelopmentControlWriteVerdict.RefusedAuthoritySite);

        // --- ... and the marker's ITEM NAME, reserved on every row of the sheet that carries it.
        //
        // The identity check above guards the marker's OWN row. This one guards the other direction,
        // which the row-scoped check cannot see: renaming an unrelated row's `ControlItem` to
        // `ControlState` creates a SECOND row the resolver treats as the marker, and because
        // `ControlItems` walks records in ascending row order the later row is the one that decides.
        // Row 11 of the candidate is a real, writable, non-envelope row, so that route was open even
        // with the marker row itself fully guarded. It is a property of the VALUE, so it is checked
        // as one — the same way the envelope-column refusal above refuses a caller-supplied column
        // name rather than a cell.
        var markerCell = DevelopmentControlAuthoritySites.MarkerIdentityCell;

        if (markerCell.LogicalSheet is not null && markerCell.LogicalColumn is not null
            && string.Equals(write.LogicalSheet, markerCell.LogicalSheet, StringComparison.Ordinal)
            && string.Equals(write.LogicalColumn, markerCell.LogicalColumn, StringComparison.Ordinal)
            && string.Equals((write.Value ?? "").Trim(), WorkbookCompatibilityReader.AuthorityMarkerItem,
                StringComparison.Ordinal))
            return new(false,
                "Refused: " + DevelopmentControlAuthoritySites.ReservedItemNameRefusalReason(
                    markerCell.LogicalSheet, markerCell.LogicalColumn,
                    WorkbookCompatibilityReader.AuthorityMarkerItem),
                null, null, DevelopmentControlWriteVerdict.RefusedAuthoritySite);

        var previous = sheet.Records
            .FirstOrDefault(r => r.Row == write.Row)?.Get(write.LogicalColumn);

        try
        {
            DevelopmentControlCellWriter.WriteCell(
                _storePath, sheet.PhysicalName, write.Row, column.PhysicalColumn, write.Value);
        }
        catch (WorkbookDecodeException ex)
        {
            // The physical guard's own refusal arrives here as a decode exception. It is reported
            // with the SAME typed verdict as the logical guard above rather than as a generic
            // failure: the two checks answer one question and a caller must not have to pattern
            // match on prose to learn that it was the governed cell that stopped the write.
            var verdict = ex.Message.StartsWith("GOVERNED_CELL", StringComparison.Ordinal)
                ? DevelopmentControlWriteVerdict.RefusedAuthoritySite
                : DevelopmentControlWriteVerdict.Unknown;

            return new(false, "Refused: " + ex.Message, previous, null, verdict);
        }

        return new(true,
            $"Wrote '{write.Value}' to {sheet.PhysicalName}!{column.PhysicalColumn}{write.Row} "
            + $"(logical {write.LogicalSheet}.{write.LogicalColumn}).",
            previous, write.Value, DevelopmentControlWriteVerdict.Allowed);
    }

    /// <summary>
    /// W8D FINAL TASK 3. The authority cutover: the ONE operation that may promote this workbook
    /// from <c>CANDIDATE</c> to <c>AUTHORITATIVE</c>.
    ///
    /// <para><b>Why this had to exist.</b> Before it, no cutover mechanism existed at all AND the
    /// promotion was reachable by an ordinary cell write — so the directive's "a normal workbook
    /// write must NOT promote authority" was not a weak guard but a false statement. The marker
    /// guard now lives in <see cref="DevelopmentControlCellWriter.WriteCell"/>; this method is the
    /// only path around it, and it is governed.</para>
    ///
    /// <para><b>The order of the refusals is the order of the questions.</b> Reservation state
    /// first, because nothing else is knowable without it. Then the attestation, because it is the
    /// one input only the operator can supply and refusing it last would waste the whole
    /// operation. Then the path class, then the schema, then the marker, then structural integrity,
    /// and only then the workbook itself. Every one of these is answered from a read taken UNDER
    /// the held lock, so the state that is checked is the state that is written.</para>
    ///
    /// <para><b>H-1E is enforced as an attestation, not assumed.</b> The Owner's policy makes
    /// cutover conditional on the shared suites being green and on both hosts interpreting the same
    /// canonical data identically. This component cannot run two hosts' suites from inside a
    /// workbook write, and it must not report a condition it never established. So the operator
    /// states both, the result records the evidence reference, and an incomplete attestation is a
    /// typed refusal rather than a default.</para>
    ///
    /// <para><b>The unresolved-decision count is measured here, not accepted from the caller.</b>
    /// H-1E permits an authoritative workbook to contain explicit <c>HUMAN_DECISION_REQUIRED</c>
    /// records, which makes that count the number that says how much is still open inside the
    /// artifact being promoted. A caller-supplied count would be an assertion about the workbook by
    /// the party asking to change it; reading it from <c>20_Decisions</c> makes it a fact about the
    /// bytes being promoted.</para>
    /// </summary>
    internal DevelopmentControlCutoverResult Cutover(DevelopmentControlCutoverRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stateBefore = DevelopmentControlAuthoritySites.Candidate;
        var stateAfter = DevelopmentControlAuthoritySites.Authoritative;

        DevelopmentControlCutoverResult Refuse(
            DevelopmentControlCutoverVerdict verdict, string reason,
            string sha = "", IReadOnlyList<DevelopmentControlCutoverSiteChange>? sites = null,
            int unresolved = 0, string integrity = "", string from = "", string to = "")
            => new(false, verdict, reason, _storePath, sha, sha, from, to,
                   sites ?? Array.Empty<DevelopmentControlCutoverSiteChange>(), unresolved, integrity,
                   request.ChangeId, request.Rationale, request.Attestation?.EvidenceRef ?? "");

        if (!Held)
            return Refuse(DevelopmentControlCutoverVerdict.RefusedNoReservation,
                "this reservation has already been released, so it no longer excludes another writer. "
                + "A cutover performed without the lock is exactly the concurrent promotion the lock "
                + "exists to prevent. Acquire a new reservation.");

        if (_lock.Lease.Expiry <= DateTimeOffset.UtcNow)
            return Refuse(DevelopmentControlCutoverVerdict.RefusedNoReservation,
                $"the lease expired at {_lock.Lease.Expiry:O}. Another process may legitimately have "
                + "reclaimed the lock, so this cutover could race a legitimate writer. Heartbeat or "
                + "re-acquire first.");

        if (request.Attestation is null || !request.Attestation.IsComplete)
            return Refuse(DevelopmentControlCutoverVerdict.RefusedAttestationIncomplete,
                "the H-1E attestation is incomplete. Cutover is permitted only where the shared "
                + "writer/reader/lock suites are green AND Forge and Developer interpret the same "
                + "canonical data identically, and this component cannot run another host's suites to "
                + "establish either. Both conditions must be stated affirmatively with an evidence "
                + "reference; an unstated precondition is not a satisfied one.");

        if (string.IsNullOrWhiteSpace(request.ChangeId))
            return Refuse(DevelopmentControlCutoverVerdict.RefusedAttestationIncomplete,
                "no authorising ChangeId was supplied. A cutover changes what the estate treats as "
                + "authoritative and must be traceable to the decision that authorised it.");

        if (string.IsNullOrWhiteSpace(request.Rationale))
            return Refuse(DevelopmentControlCutoverVerdict.RefusedAttestationIncomplete,
                "no rationale was supplied. The cutover is the one operation that changes authority, "
                + "so why it was performed is part of the governed record rather than a commit message.");

        if (request.FromState != stateBefore || request.ToState != stateAfter)
            return Refuse(DevelopmentControlCutoverVerdict.RefusedAttestationIncomplete,
                $"the request declares a transition '{request.FromState}' -> '{request.ToState}', which "
                + $"is not the governed transition '{stateBefore}' -> '{stateAfter}'. The endpoint states "
                + "are properties of the model, not parameters of the request.");

        var pathCheck = WorkbookCompatibilityReader.AuthorizeWritePath(_storePath);
        if (!pathCheck.Allowed)
            return Refuse(DevelopmentControlCutoverVerdict.RefusedPath,
                $"the store path is a refused class. {pathCheck.Reason}");

        // --- everything below reads UNDER the held lock, so what is verified is what is written.
        var read = WorkbookCompatibilityReader.Read(_storePath);

        if (read.Form != WorkbookForm.V3)
            return Refuse(DevelopmentControlCutoverVerdict.RefusedNotV3,
                $"the workbook reads as {read.Form}, not V3. Only the 26-sheet V3 model carries the "
                + "cutover sites, so there is nothing here to promote.");

        var authorisation = WorkbookCompatibilityReader.AuthorizeWrite(read, WorkbookForm.V3);
        if (!authorisation.Allowed)
            return Refuse(DevelopmentControlCutoverVerdict.RefusedSchemaMismatch,
                $"the workbook may not be written at all, so it may not be promoted. {authorisation.Reason}");

        var marker = WorkbookCompatibilityReader.AuthorityMarker(read);
        if (string.IsNullOrWhiteSpace(marker))
            return Refuse(DevelopmentControlCutoverVerdict.RefusedMarkerUnreadable,
                $"`00_Control` carries no readable {WorkbookCompatibilityReader.AuthorityMarkerItem}, so "
                + "the before-state cannot be established. A cutover that cannot read the state it is "
                + "moving from cannot report what it moved.");

        var integrity = StructuralIntegrity(read);

        if (marker == stateAfter)
            return Refuse(DevelopmentControlCutoverVerdict.RefusedAlreadyAuthoritative,
                $"every authority site already reads '{stateAfter}'. Nothing to do; re-running a cutover "
                + "is a no-op rather than a second promotion.", sha: read.SourceSha256, integrity: integrity,
                from: stateAfter, to: stateAfter);

        if (read.GovernanceSubstrateAbsent.Count > 0)
        {
            // Reported, never used to refuse. H-1E settled this: a V3 workbook with an absent
            // governance substrate MAY hold authority provided both source states are preserved,
            // provenance is retained, the unresolved status is explicit, and integrity is green.
            // Refusing here would re-decide a question the Owner has answered.
            integrity += $" | governance-substrate sheets absent: "
                + string.Join(", ", read.GovernanceSubstrateAbsent)
                + " (H-1E: reported, not disqualifying)";
        }

        // `PartiallySupported` is NOT a refusal. That is precisely the V3-with-absent-substrate
        // result, which H-1E settled as permitted to hold authority. Refusing it here would
        // re-decide the question the Owner has already answered, and would make every cutover
        // unreachable on the artifact this cutover exists to promote.
        if (read.Result is ReaderResult.UnsupportedSchema or ReaderResult.Corrupt or ReaderResult.EmptyValid
            || read.Sheets.Any(s => s.Presence == SheetPresence.DeclaredButMissing))
        {
            var missing = read.Sheets.Where(s => s.Presence == SheetPresence.DeclaredButMissing)
                .Select(s => s.LogicalName).ToArray();
            return Refuse(DevelopmentControlCutoverVerdict.RefusedStructuralIntegrity,
                $"structural integrity did not pass: result={read.Result}"
                + (missing.Length > 0
                    ? $"; sheets declared but missing: {string.Join(", ", missing)}"
                    : "")
                + ". A candidate that is not whole must not be promoted, because promotion is what "
                + "makes every subsequent reader trust it.", sha: read.SourceSha256, integrity: integrity);
        }

        var decisions = read.Sheet("Decisions");
        var unresolvedCount = decisions is null
            ? 0
            : decisions.Records.Count(r =>
                (r.Get("Decision") ?? "").TrimStart()
                    .StartsWith("HUMAN_DECISION_REQUIRED", StringComparison.OrdinalIgnoreCase));

        var shaBefore = read.SourceSha256;

        IReadOnlyList<DevelopmentControlCutoverSiteChange> changes;
        try
        {
            changes = DevelopmentControlCellWriter.CutoverAuthority(_storePath, stateBefore, stateAfter);
        }
        catch (WorkbookDecodeException ex)
        {
            var verdict = ex.Message.StartsWith("CUTOVER_SITE_MISSING", StringComparison.Ordinal)
                ? DevelopmentControlCutoverVerdict.RefusedSiteMissing
                : DevelopmentControlCutoverVerdict.RefusedSiteValueUnexpected;

            return Refuse(verdict, "the cutover was refused and the workbook was left unmodified. " + ex.Message,
                sha: shaBefore, unresolved: unresolvedCount, integrity: integrity, from: stateBefore);
        }

        // --- read-back. The swap is atomic, but "the file changed" and "the workbook now reads as
        // authoritative through the same reader every host uses" are different claims, and only the
        // second one is what the cutover is supposed to establish.
        var after = WorkbookCompatibilityReader.Read(_storePath);
        var markerAfter = WorkbookCompatibilityReader.AuthorityMarker(after);

        if (markerAfter != stateAfter)
            return Refuse(DevelopmentControlCutoverVerdict.RefusedReadBackMismatch,
                $"the four sites were written, but re-reading through the canonical reader reports "
                + $"'{markerAfter}' rather than '{stateAfter}'. The workbook on disk has changed; the "
                + "authority did not. Treat the artifact as un-promoted until this is explained.",
                sha: after.SourceSha256, sites: changes, unresolved: unresolvedCount,
                integrity: integrity, from: stateBefore, to: markerAfter ?? "");

        return new DevelopmentControlCutoverResult(
            Performed: true,
            Verdict: DevelopmentControlCutoverVerdict.Performed,
            Reason: $"cutover performed. {changes.Count} authority site(s) moved {stateBefore} -> {stateAfter} "
                  + "in one atomic container swap; read-back through the canonical reader confirms "
                  + $"'{stateAfter}'. {unresolvedCount} explicit HUMAN_DECISION_REQUIRED record(s) remain "
                  + "inside the now-authoritative control model, which H-1E permits.",
            Path: _storePath,
            Sha256Before: shaBefore,
            Sha256After: after.SourceSha256,
            StateBefore: stateBefore,
            StateAfter: stateAfter,
            Sites: changes,
            UnresolvedDecisionCount: unresolvedCount,
            StructuralIntegrityReport: integrity,
            ChangeId: request.ChangeId,
            Rationale: request.Rationale,
            EvidenceRef: request.Attestation.EvidenceRef);
    }

    /// <summary>
    /// The integrity facts the read model can establish, named as what they are rather than
    /// summarised as "green". Every field here is a measured property of the workbook in front of
    /// us; the cell-level and formula-level validation is the validator's job and is recorded in the
    /// cutover evidence alongside this string, not folded into it.
    ///
    /// <para><b>These counts are LOGICAL BINDINGS, not physical sheets.</b> The read model projects
    /// 19 logical names onto the 26-sheet physical model; the two numbers are different and
    /// conflating them would misstate the artifact. The string says "logical bindings" for that
    /// reason, and the physical sheet count is deliberately NOT asserted here because this model
    /// does not measure it — the structural validator does, and its output is cutover evidence.</para>
    /// </summary>
    private static string StructuralIntegrity(WorkbookReadResult read)
    {
        var bound = read.Sheets.Count(s => s.Presence == SheetPresence.Bound);
        var missing = read.Sheets.Count(s => s.Presence == SheetPresence.DeclaredButMissing);
        var unbound = read.Sheets.Count - bound - missing;

        return $"result={read.Result}; form={read.Form}; logical bindings={read.Sheets.Count} "
             + $"(bound={bound}, declared-but-missing={missing}, unbound={unbound}; "
             + "physical sheet count is not measured by this read model); "
             + $"sha256={read.SourceSha256}";
    }

    public void Dispose()
    {
        if (Held) Release();
        _lock.Dispose();
    }

    /// <summary>
    /// TASK 1 / TASK 2. Appends one record to a sheet that already exists, under this held
    /// reservation and the lock behind it.
    ///
    /// <para><b>Every refusal here names a condition, and the conditions are ordered by what the
    /// caller can do about them.</b> Reservation state first, because nothing else is knowable
    /// without it. Then the declared scope, because a change that does not cover this store has no
    /// business writing to it whatever the workbook contains — and answering that from the
    /// declaration alone means a mis-scoped change never even reads the authority. Then the
    /// request's own internal consistency (identity, provenance). Only then the workbook: sheet,
    /// envelope, columns, collision. A single generic failure would leave a caller unable to tell
    /// "I declared the wrong scope" from "the workbook moved".</para>
    ///
    /// <para><b>The read happens UNDER the held lock, and so does the read-back.</b> Resolving the
    /// envelope and the columns against a read taken before the lock was held would let the
    /// workbook change between resolution and append, so the record would land at a position that
    /// no longer means what the caller intended. That window is the whole reason the lock exists,
    /// so it is closed on both sides of the write rather than only the near one.</para>
    /// </summary>
    internal DevelopmentControlAppendResult Append(DevelopmentControlAppendRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!Held)
            return Refuse("this reservation has already been released, so it no longer excludes "
                        + "another writer. Acquire a new one.");

        if (_lock.Lease.Expiry <= DateTimeOffset.UtcNow)
            return Refuse($"the lease expired at {_lock.Lease.Expiry:O}. Appending under an expired "
                        + "lease means another process may legitimately have reclaimed the lock, so "
                        + "this record could race a legitimate writer. Heartbeat or re-acquire first.");

        var containment = ScopeCoversStore(record.DeclaredScope, out var scopeReason);
        if (containment != ChangeScopeContainmentVerdict.WithinDeclaredScope)
            return Refuse(scopeReason, containment);

        // From here on the containment question HAS been answered, in the affirmative, so every
        // refusal below reports WithinDeclaredScope rather than NotEvaluated. That is the whole
        // point of the value being typed: a caller that sees "refused, and the scope was fine"
        // knows the defect is in the record or the workbook and not in its own declaration,
        // which is exactly the distinction the prose alone could not carry across the boundary.
        DevelopmentControlAppendResult RefuseAfterScope(string reason) => Refuse(reason, containment);

        if (string.IsNullOrWhiteSpace(record.IdentityColumn))
            return RefuseAfterScope("no identity column was declared. Without it the duplicate-identity check "
                        + "cannot run, and an append whose collision check silently does not run is "
                        + "how one immutable id enters the ledger twice.");

        if (string.IsNullOrWhiteSpace(record.ChangeId))
            return RefuseAfterScope("no authorising ChangeId was supplied. Every governed record states the "
                        + "change it belongs to; a record with a blank originating change cannot be "
                        + "traced back to the decision that authorised it.");

        // --- the record must belong to the change whose scope authorises it.
        //
        // Containment answers "does this declaration cover the store?". It says nothing about whether
        // the RECORD belongs to the change that made the declaration. Without this check a caller
        // could declare a scope for one change and stamp the record with another, so the envelope
        // would carry an originating change that never authorised the write while read-back reported
        // the append as within a scope it was never part of. Reproduced before this guard existed: a
        // record stamped 'UNRELATED-CHANGE' was accepted under a scope declaring 'CHG-VERIFY', and
        // the result came back WithinDeclaredScope.
        //
        // The verdict is ScopeAmendmentRequired rather than RefuseAfterScope's WithinDeclaredScope,
        // and that is deliberate: containment HAS passed, so reporting WithinDeclaredScope would tell
        // the caller its declaration was the problem when the fault is the record's own change id.
        // ScopeAmendmentRequired says the true thing — the scope must name this change, or the record
        // must be restamped. DeclaredScope is non-null here: ScopeCoversStore returned
        // WithinDeclaredScope above, which it can only do for a non-null declaration.
        if (!string.Equals(record.ChangeId, record.DeclaredScope.ChangeId, StringComparison.Ordinal))
            return Refuse($"SCOPE_CHANGE_REQUIRED: the record is stamped with change '{record.ChangeId}' "
                        + $"but its declared scope authorises change '{record.DeclaredScope.ChangeId}'. A "
                        + "record may only be written under the change whose scope declares it, "
                        + "otherwise its envelope names an origin that never authorised it.",
                ChangeScopeContainmentVerdict.ScopeAmendmentRequired);

        if (record.Provenance is null)
            return RefuseAfterScope("no provenance was supplied. Every governed V3 record states where it came "
                        + "from — a blank migration envelope is an unfilled field, not a native record.");

        if (!record.Provenance.IsWellFormed(out var provenanceReason))
            return RefuseAfterScope("provenance is not well-formed. " + provenanceReason);

        var read = WorkbookCompatibilityReader.Read(_storePath);

        // --- the same write-authorisation gate a cell write passes.
        //
        // An append IS a write to the authority, so it is not exempt from the gate that decides
        // whether this form may be written at all. Without this the two preserved 14-sheet legacy
        // revisions would be appendable — not because anything about them changed, but because this
        // path would simply never have asked. The gate is what turns "the V3 model may be written
        // and the frozen forms may not" from a fact about the caller into a property of the
        // component.
        var authorisation = WorkbookCompatibilityReader.AuthorizeWrite(read, WorkbookForm.V3);
        if (!authorisation.Allowed)
            return RefuseAfterScope($"this workbook may not be appended to. {authorisation.Reason}");

        var sheet = read.Sheets.FirstOrDefault(s =>
            string.Equals(s.LogicalName, record.LogicalSheet, StringComparison.Ordinal));

        if (sheet is null || sheet.Presence != SheetPresence.Bound)
            return RefuseAfterScope($"no readable sheet is bound to logical name '{record.LogicalSheet}'. "
                        + $"Available: {string.Join(", ", read.Sheets.Select(s => s.LogicalName))}.");

        if (sheet.PhysicalName is null)
            return RefuseAfterScope($"'{record.LogicalSheet}' resolved to no physical sheet.");

        // --- the envelope, resolved against this sheet's OWN header row.
        //
        // Not against a table of assumed column letters: the envelope's position is a convention of
        // the form, but which columns the form actually spells is a property of the sheet in front
        // of us, and 25_Dashboard carries no envelope at all. Resolving by name is also what makes a
        // sheet that has been restructured underneath this code a refusal rather than a write.
        var envelope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missingEnvelope = new List<string>();

        foreach (var logicalName in DevelopmentControlEnvelopeColumns.All)
        {
            var column = sheet.Columns.FirstOrDefault(c =>
                string.Equals(c.LogicalName, logicalName, StringComparison.Ordinal)
                && c.Found && c.PhysicalColumn is not null);

            if (column?.PhysicalColumn is null) missingEnvelope.Add(logicalName);
            else envelope[logicalName] = column.PhysicalColumn;
        }

        if (missingEnvelope.Count > 0)
            return RefuseAfterScope($"'{record.LogicalSheet}' does not carry the full governance envelope — "
                        + $"{string.Join(", ", missingEnvelope)} did not resolve against its header "
                        + "row. Appending here would create a record with no position in the "
                        + "append-only trail and no stated origin, which is an implicit disposition.");

        // --- the caller's values.
        var cells = new List<AppendCell>();
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (logicalColumn, value) in record.Values ?? new Dictionary<string, string>())
        {
            if (DevelopmentControlEnvelopeColumns.All.Contains(logicalColumn, StringComparer.OrdinalIgnoreCase))
                return RefuseAfterScope($"'{logicalColumn}' is an envelope column, and the envelope is written "
                            + "by this component rather than supplied by the caller. A caller able "
                            + "to set IsCurrent or the originating ChangeId could place a record "
                            + "anywhere in the trail it chose.");

            var column = sheet.Columns.FirstOrDefault(c =>
                string.Equals(c.LogicalName, logicalColumn, StringComparison.Ordinal)
                && c.Found && c.PhysicalColumn is not null);

            if (column?.PhysicalColumn is null)
                return RefuseAfterScope($"'{record.LogicalSheet}' has no logical column '{logicalColumn}'. "
                            + "Available: "
                            + string.Join(", ", sheet.Columns.Where(c => c is { Found: true, PhysicalColumn: not null })
                                                            .Select(c => c.LogicalName)) + ".");

            resolved[logicalColumn] = column.PhysicalColumn;
            cells.Add(new AppendCell(column.PhysicalColumn, value ?? ""));
        }

        // --- the authority marker's ITEM NAME is reserved on the sheet that carries it.
        //
        // `ControlItems` does not read the marker from a fixed address: it scans `00_Control` and
        // keeps the row whose `ControlItem` column reads `ControlState`, assigning into a plain
        // dictionary as it walks the records in ASCENDING ROW ORDER. So a second row declaring that
        // item name is a second marker, and the LATER row is the one that resolves — the original
        // row is left untouched and correct while the workbook's authority is decided by the
        // appended one. Measured before this refusal existed, on both fixtures: an ordinary append
        // to `Control` declaring an `IdentityColumn` of `Value` with a unique value and a
        // `ControlItem` of `ControlState` PROMOTED a candidate workbook and DEMOTED an authoritative
        // one, passing every gate on the way — the collision check never asks whether the VALUE is a
        // marker, only whether the nominated identity repeats.
        //
        // This sits with the envelope-column refusal above rather than with the identity block below
        // because it is the same kind of question — a property of the RECORD'S OWN CONTENT, answerable
        // before the workbook's identity model is consulted. `ControlItems` also fails closed on a
        // duplicated key, which covers anything reaching the bytes by another route; this half is
        // what keeps the bytes from changing at all, and a duplicate the reader ignores is still a
        // workbook two tools disagree about.
        var markerCell = DevelopmentControlAuthoritySites.MarkerIdentityCell;

        if (markerCell.LogicalSheet is not null && markerCell.LogicalColumn is not null
            && string.Equals(record.LogicalSheet, markerCell.LogicalSheet, StringComparison.Ordinal)
            && record.Values is not null
            && record.Values.Any(kv =>
                string.Equals(kv.Key, markerCell.LogicalColumn, StringComparison.OrdinalIgnoreCase)
                && string.Equals((kv.Value ?? "").Trim(), WorkbookCompatibilityReader.AuthorityMarkerItem,
                    StringComparison.Ordinal)))
            return RefuseAfterScope(DevelopmentControlAuthoritySites.ReservedItemNameRefusalReason(
                        markerCell.LogicalSheet, markerCell.LogicalColumn,
                        WorkbookCompatibilityReader.AuthorityMarkerItem)
                    + " An appended record may not declare it.");

        // --- IMMUTABLE IDENTITY. Owned by the schema, never by the caller.
        //
        // Everything below reads the sheet's declared identity (`SheetBinding.V3ImmutableIdentity`)
        // rather than `record.IdentityColumn`. That member is retained on the public contract as a
        // DECLARATION to be validated, never as the authority for which column holds identity —
        // which is what it effectively was before: the blank check and the collision check both read
        // whichever column the caller nominated while the row was written into the sheet's real key
        // column. Nominating "Branch" therefore redirected both guards away from `LineageId`, and a
        // duplicate AND a blank LineageId were each accepted on the live authority and on the
        // archived candidate alike. Substituting a non-key column is no longer a way to choose which
        // value has to be unique.
        var binding = WorkbookCompatibilityMap.Sheets.FirstOrDefault(b =>
            string.Equals(b.LogicalName, record.LogicalSheet, StringComparison.Ordinal));

        var immutableIdentity = binding?.V3ImmutableIdentity;

        if (immutableIdentity is null || immutableIdentity.Count == 0)
            return RefuseAfterScope($"'{record.LogicalSheet}' declares no immutable record identity, so a "
                        + "record appended here could not be checked for collision and its key could "
                        + "never be shown unique. This sheet is not an append target. Append targets: "
                        + string.Join(", ", WorkbookCompatibilityMap.Sheets
                            .Where(b => b.V3ImmutableIdentity is { Count: > 0 })
                            .Select(b => b.LogicalName)) + ".");

        if (!immutableIdentity.Contains(record.IdentityColumn, StringComparer.Ordinal))
            return RefuseAfterScope($"the identity column '{record.IdentityColumn}' is not the immutable "
                        + $"identity this schema declares for '{record.LogicalSheet}', which is "
                        + $"{string.Join(" + ", immutableIdentity)}. Identity is a property of the "
                        + "schema, so a caller cannot nominate a non-key column and redirect the "
                        + "collision check away from the value that has to be unique.");

        // Every key column must be supplied and non-blank, checked over the SCHEMA's columns — so a
        // composite key cannot be half-satisfied by supplying only one of its members.
        var keyValues = new List<string>();
        foreach (var keyColumn in immutableIdentity)
        {
            if (!resolved.ContainsKey(keyColumn))
                // Keeps the wording this refusal has always used ("the identity column 'X' is not among
                // the values supplied"), so the pre-existing assertion in
                // Append_Refuses_AValuesMapWithoutTheDeclaredIdentityColumn still pins it. The message
                // only gained the schema's declaration, because with a composite key the caller needs
                // to be told which other columns the sheet's identity also requires.
                return RefuseAfterScope($"the identity column '{keyColumn}' is not among the values "
                            + "supplied, so the record would have no identity and the collision check "
                            + $"would have nothing to compare. '{record.LogicalSheet}' declares "
                            + $"{string.Join(" + ", immutableIdentity)} as its immutable identity.");

            var keyValue = record.Values!
                .First(kv => string.Equals(kv.Key, keyColumn, StringComparison.OrdinalIgnoreCase))
                .Value;

            if (string.IsNullOrWhiteSpace(keyValue))
                return RefuseAfterScope($"the identity column '{keyColumn}' was supplied blank. A blank key "
                            + "collides with every other blank key and identifies nothing.");

            keyValues.Add(keyValue);
        }

        // --- immutable identity. Checked against EVERY row, not only current ones: the point of an
        // immutable id is that it is never reused, so a superseded row still owns its value. A
        // composite key is compared as a TUPLE, because no single member of it is unique — SourceId
        // alone would refuse a legitimate second edge between the same pair, and any single member
        // would admit a duplicate edge that differs only in its relation.
        var keyByColumn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < immutableIdentity.Count; i++) keyByColumn[immutableIdentity[i]] = keyValues[i];

        // The identity as one string, for the result's RecordKey and for the read-back below. For a
        // single-column key this is just the value, so existing callers and messages are unchanged.
        var identityValue = string.Join(" + ", keyValues);

        // A human-readable form naming each column, used in every refusal and result sentence. A
        // composite key reported as a bare joined value would be ambiguous about which part is which.
        var identityLabel = string.Join(" + ",
            immutableIdentity.Select((c, i) => $"{c} = '{keyValues[i]}'"));

        var collision = sheet.Records.FirstOrDefault(r =>
            immutableIdentity.All(c =>
                string.Equals(r.Get(c), keyByColumn[c], StringComparison.OrdinalIgnoreCase)));

        if (collision is not null)
            return RefuseAfterScope($"a record with {identityLabel} already exists on "
                        + $"'{record.LogicalSheet}' at row {collision.Row}. Record identity is "
                        + "immutable, so this is a collision and not an update — the same id must not "
                        + "name two records.");

        // --- the envelope values this component owns.
        var effectiveFrom = (record.EffectiveFrom ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var provenance = record.Provenance;

        var envelopeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DevelopmentControlEnvelopeColumns.RecordVersion] = record.RecordVersion,
            [DevelopmentControlEnvelopeColumns.IsCurrent] = "Yes",
            [DevelopmentControlEnvelopeColumns.EffectiveFrom] =
                effectiveFrom.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            [DevelopmentControlEnvelopeColumns.ChangeId] = record.ChangeId,
            [DevelopmentControlEnvelopeColumns.SupersedesVersion] = record.SupersedesVersion,
            [DevelopmentControlEnvelopeColumns.SourceForm] = provenance.SourceForm,
            [DevelopmentControlEnvelopeColumns.SourceWorkbook] = provenance.SourceWorkbook,
            [DevelopmentControlEnvelopeColumns.SourceWorkbookHash] = provenance.SourceWorkbookHash,
            [DevelopmentControlEnvelopeColumns.SourceSheet] = provenance.SourceSheet,
            [DevelopmentControlEnvelopeColumns.SourceRecordId] = provenance.SourceRecordId,
            [DevelopmentControlEnvelopeColumns.SourceRevision] = provenance.SourceRevision,
            [DevelopmentControlEnvelopeColumns.SourceArchitectureVersion] = provenance.SourceArchitectureVersion,
            [DevelopmentControlEnvelopeColumns.MigrationTimestamp] = provenance.MigrationTimestamp,
            [DevelopmentControlEnvelopeColumns.MigrationTransformation] = provenance.MigrationTransformation,
        };

        foreach (var (logicalName, value) in envelopeValues)
            cells.Add(new AppendCell(envelope[logicalName], value));

        int row;
        try
        {
            row = DevelopmentControlCellWriter.AppendRow(_storePath, sheet.PhysicalName, cells);
        }
        catch (WorkbookDecodeException ex)
        {
            return Refuse(ex.Message);
        }

        // --- read-back, projected through the SAME contract record a host reads, so what is
        // verified here is what a host will see rather than an implementation-shaped near-equivalent.
        var after = WorkbookCompatibilityReader.Read(_storePath);
        var appended = after.Records
            .Where(r => string.Equals(r.LogicalSheet, record.LogicalSheet, StringComparison.Ordinal)
                     && immutableIdentity.All(c =>
                            string.Equals(r.Get(c), keyByColumn[c], StringComparison.OrdinalIgnoreCase)))
            .Select(r => new DevelopmentControlRecord(r.LogicalSheet, r.Row, r.Values))
            .FirstOrDefault();

        if (appended is null)
        {
            // The write HAPPENED, so Appended is true — reporting false would misdescribe the
            // workbook. But the record could not be read back at the identity just written, which
            // means the artifact and this component disagree; that must be said, not smoothed over.
            return new DevelopmentControlAppendResult(true,
                $"Wrote row {row} of {sheet.PhysicalName} but the read-back did not find "
                + $"{identityLabel}. The record is in the workbook and "
                + "did NOT verify — treat it as UNVERIFIED and re-read the authority before relying "
                + "on it.",
                row, identityValue, null, null)
            { Containment = ChangeScopeContainmentVerdict.WithinDeclaredScope };
        }

        return new DevelopmentControlAppendResult(true,
            $"Appended {identityLabel} to {sheet.PhysicalName} "
            + $"(logical {record.LogicalSheet}) at row {appended.Row}, under change "
            + $"'{record.ChangeId}', and read it back through the shared reader.",
            appended.Row, identityValue, appended.Provenance, appended.Envelope)
        { Containment = containment };
    }

    /// <summary>Refuses before the containment question was asked, so the verdict says so.</summary>
    private static DevelopmentControlAppendResult Refuse(string reason) =>
        Refuse(reason, ChangeScopeContainmentVerdict.NotEvaluated);

    private static DevelopmentControlAppendResult Refuse(
        string reason, ChangeScopeContainmentVerdict containment) =>
        new(false, "Refused: " + reason, null, null, null, null) { Containment = containment };

    /// <summary>
    /// Whether <paramref name="declaration"/> claims this control store for WRITE, as the typed
    /// verdict the caller publishes on its result rather than as a bare bool.
    ///
    /// <para><b>Fail-closed in three separate ways.</b> A missing declaration, a declaration with no
    /// <see cref="ChangeScopeItemKind.ControlStore"/> item, and a ControlStore item declared
    /// <see cref="ChangeScopeAccessMode.Read"/> all refuse. The read case is the one worth stating:
    /// declaring that you will read the authority is not a claim to write it, and a check that
    /// accepted any ControlStore item regardless of access mode would authorise exactly the appends
    /// whose declaration said the lane would not modify the store.</para>
    ///
    /// <para><b>Why this is enforced here and not only in the collision policy.</b> The collision
    /// policy answers "do two lanes conflict"; it does not answer "does this lane cover this store".
    /// Those are different questions, and the second has no other asker — an append that consults
    /// only the collision engine would write to a store no declaration had ever mentioned.</para>
    /// </summary>
    private ChangeScopeContainmentVerdict ScopeCoversStore(
        ChangeScopeDeclaration? declaration, out string reason)
    {
        if (declaration is null)
        {
            reason = "no ChangeScope declaration was supplied. The append cannot show that this "
                   + "change is entitled to modify the control store, so it does not.";
            return ChangeScopeContainmentVerdict.ScopeAmendmentRequired;
        }

        var items = declaration.Items ?? [];
        var writes = items.Where(i => i.Kind == ChangeScopeItemKind.ControlStore
                                   && i.AccessMode == ChangeScopeAccessMode.Write).ToArray();

        if (writes.Length == 0)
        {
            var anyControlStore = items.Any(i => i.Kind == ChangeScopeItemKind.ControlStore);
            reason = anyControlStore
                ? $"the ChangeScope '{declaration.Lane}' declares this control store for READ, not "
                + "write. A lane that has declared it will not modify the authority is not entitled "
                + "to append a record to it."
                : $"the ChangeScope '{declaration.Lane}' declares no ControlStore item at all, so it "
                + "does not cover the control store — whatever else it covers.";
            return ChangeScopeContainmentVerdict.ScopeAmendmentRequired;
        }

        if (!writes.Any(i => MatchesStoreTarget(i.Target, _storePath)))
        {
            reason = $"the ChangeScope '{declaration.Lane}' declares a control store, but not THIS "
                   + $"one: its ControlStore targets name "
                   + $"{string.Join(", ", writes.Select(i => $"'{i.Target}'"))}, and the store is "
                   + $"'{Path.GetFileName(_storePath)}'. A scope that names a different authority "
                   + "does not authorise this one.";
            return ChangeScopeContainmentVerdict.ScopeAmendmentRequired;
        }

        reason = "";
        return ChangeScopeContainmentVerdict.WithinDeclaredScope;
    }

    /// <summary>
    /// Whether a declared ControlStore target names this workbook. Accepts the full path, the bare
    /// file name, or a trailing-<c>*</c> prefix of the file name.
    ///
    /// <para>Deliberately narrow. A substring or case-folded "contains" match would let a scope
    /// naming <c>NEXUS_DEVELOPMENT_CONTROL_OLD.xlsx</c> authorise a write to
    /// <c>NEXUS_DEVELOPMENT_CONTROL.xlsx</c> — and on this estate both exist, which is the whole
    /// reason two 14-sheet revisions are still on disk. Prefix matching is offered because a lane
    /// legitimately declares "the DevelopmentControl family"; substring matching is not, because it
    /// cannot tell the family from a backup of it.</para>
    /// </summary>
    private static bool MatchesStoreTarget(string? target, string storePath)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;

        var name = Path.GetFileName(storePath);

        if (string.Equals(target, storePath, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(target, name, StringComparison.OrdinalIgnoreCase)) return true;

        return target.EndsWith('*')
            && name.StartsWith(target[..^1], StringComparison.OrdinalIgnoreCase);
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
