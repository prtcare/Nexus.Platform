using Nexus.DevelopmentControl.Safety;
using Nexus.ProductCore.Contracts.DevelopmentControl;
using Nexus.ProductCore.Core.DevelopmentControl;
using Xunit;

namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// W8D IMPLEMENTATION COMPLETION TASK 3 — the append refusals, each with a control that proves the
/// test can fail.
///
/// <para><b>What TASK 3 asks for, verbatim:</b> "Prove append refuses: legacy 14-sheet authority;
/// unknown sheet/table; duplicate immutable ID; invalid schema fields; append outside ChangeScope;
/// write without reservation; simultaneous conflicting writer; authority-marker mutation;
/// structure-changing operation not permitted by schema. Add mutation/negative controls so these
/// tests can actually fail."</para>
///
/// <para><b>Why every negative test carries a positive twin.</b> A test that asserts
/// <c>Appended == false</c> passes for ANY reason — a mistyped sheet name, a fixture that failed to
/// copy, a lock that was never acquired. It would report "append correctly refused the legacy
/// authority" while actually measuring "append refused everything". So each refusal below is
/// asserted twice over: the reason must name the intended condition, AND the identical request with
/// only that condition removed must succeed. The second assertion is what makes the first mean
/// something.</para>
///
/// <para><b>Nothing here writes to the real authority.</b> Every test copies the workbook to a
/// throwaway directory. <c>GitLineage</c> is the append target throughout because it carries 26
/// header cells on row 4 and ZERO data rows, so a successful append creates the sheet's first
/// record at row 5 — which is the condition TASK 8 needs and the one most likely to break, since
/// every other sheet already has rows to inherit structure from.</para>
/// </summary>
// W10.0A FINAL — LANE: WindowsOnly, at CLASS level, and deliberately a slight over-approximation.
//
// Nearly every test here exercises the append path, and the append path acquires a reservation
// through `AtomicWriterLock.LockPathFor`, which derives its lock file name from
// `DevelopmentControlStoreIdentity.CanonicalFromWorkbookPath`. That normaliser is Windows-shaped BY
// CONSTRUCTION — separators fold to `\`, the anchor is a drive root or a UNC share, and
// `IsFullyQualifiedLocal` requires `X:\`. MEASURED, not inferred: fed the literal
// `/tmp/nexus-fixture/NEXUS_DEVELOPMENT_CONTROL.xlsx` it refuses with "Store path must be fully
// qualified; '/tmp/…' is relative" — on a Windows host, exactly as it would on Linux. The component
// is the canonical DevelopmentControl lock for Nexus Forge and Nexus.Developer, both Windows desktop
// hosts, and it has no consumer for a POSIX path grammar.
//
// WHY THE CLASS RATHER THAN THE METHOD. A method-level split was attempted and measured INCOMPLETE:
// the local `Append(fixture, …)` helper holds the reservation, so "does this test acquire a lock" is
// a property of transitive call shape, not of the method body — 25 further callers were missed by a
// body-level scan. A classification that is quietly wrong is worse than one that is openly broad, so
// this is the conservative superset: a handful of read-only tests here (the marker-shadowing probes,
// which write into the container directly and take no lock) could still run portably and are moved
// to the Windows lane with the rest.
//
// Nothing is skipped inside a lane: the Windows lane runs this class, and the portable lane does not
// claim it. See PORTABLE_TEST_ARCHITECTURE.md for the two-lane contract and for the one production
// change that would move the whole class into portable CI.
[Trait("Lane", "WindowsOnly")]
public sealed class DevelopmentControlAppendSafetyTests
{
    /// <summary>
    /// The reconciled V3 candidate, preserved as a CANDIDATE-state revision. The cutover's positive
    /// path can only be exercised against a workbook whose four authority sites still read
    /// <c>CANDIDATE</c>, and the promotion consumed the only such workbook that used to exist at the
    /// canonical path. Before the W8D cutover this pointed at the live canonical file, which WAS that
    /// candidate. It now names the preserved revision instead.
    ///
    /// <para>Pointing it back at the live workbook would not strengthen anything — it would make every
    /// cutover assertion in this file unsatisfiable. The contract correctly refuses to promote an
    /// already-authoritative workbook (<c>CutoverVerdict.RefusedAlreadyAuthoritative</c>, "Nothing to
    /// do; re-running a cutover is a no-op rather than a second promotion"). That refusal is the
    /// guarantee, not a defect: there is no second cutover.</para>
    /// </summary>
    /// <summary>
    /// W10.0A FINAL: this was the estate's preserved pre-cutover revision at an absolute
    /// <c>D:\NEXUS\…</c> path, overridable by its own environment variable. It is now a GENERATED
    /// fixture. The assertions below are unchanged — they are about how the append and cutover paths
    /// treat a V3 CANDIDATE workbook, which is a property of the schema, not of the file's address.
    /// See <see cref="WorkbookFixtures"/> and <see cref="TestEstate"/>.
    /// </summary>
    private static string CandidateWorkbook => WorkbookFixtures.Candidate;

    /// <summary>
    /// The estate's live canonical workbook — since the cutover, the authority itself.
    ///
    /// <para>
    /// W10.0A FINAL: resolved through the one governed boundary rather than a hardcoded path. The
    /// single test that asserts a property of the <b>live</b> file is
    /// <c>TheLiveCanonicalWorkbook_IsTheAuthority_AndReadsAsAuthoritative</c>, and it is the only
    /// caller; every other test in this file proves contract behaviour against the generated
    /// fixture and must not need a machine with an estate on it.
    /// </para>
    /// </summary>
    private static string V3Workbook => Path.Combine(
        TestEstate.Root, "DevelopmentControl", "NEXUS_DEVELOPMENT_CONTROL.xlsx");

    /// <summary>
    /// The legacy form. Read-only in every sense: tests copy it, and the component must refuse to
    /// append to the copy.
    ///
    /// <para>
    /// W10.0A FINAL: generated rather than read from the preserved estate revision. The legacy form is
    /// identified by two sheet NAMES, so a two-sheet workbook built from those names is the same form
    /// to every reader and to the write gate — which is what the refusals below assert.
    /// </para>
    /// </summary>
    private static string LegacyWorkbook => WorkbookFixtures.Legacy;

    private static readonly object EvidenceGate = new();

    private static void Record(string line)
    {
        var path = Environment.GetEnvironmentVariable("W1_T10_EVIDENCE");
        if (string.IsNullOrWhiteSpace(path)) return;

        lock (EvidenceGate)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Copy { get; }
        public string LockDir { get; }
        public string Sha256() => WorkbookCompatibilityReader.Sha256Of(Copy).ToUpperInvariant();

        public Fixture(string source)
        {
            Root = Path.Combine(Path.GetTempPath(), "w8d-append-" + Guid.NewGuid().ToString("N")[..10]);
            Directory.CreateDirectory(Root);
            Copy = Path.Combine(Root, "NEXUS_DEVELOPMENT_CONTROL.xlsx");
            File.Copy(source, Copy, overwrite: false);
            // The CANONICAL lock directory: beside the workbook it guards, derived from it. A
            // caller-named directory is no longer accepted — that is how two writers came to hold two
            // files for one identity.
            LockDir = AtomicWriterLock.CanonicalLockDirectoryFor(Copy);
            Directory.CreateDirectory(LockDir);
        }

        public static Fixture Candidate() => new(CandidateWorkbook);

        /// <summary>
        /// The LIVE canonical workbook. Estate-backed by construction — the only factory here that is,
        /// and it exists because exactly one test asserts a property of the real file.
        /// </summary>
        public static Fixture LiveAuthority() => new(V3Workbook);

        /// <summary>
        /// A generated AUTHORITATIVE V3 workbook. This is the portable twin of
        /// <see cref="LiveAuthority"/>: same form, same authority, same marker, no estate required.
        /// </summary>
        public static Fixture AuthoritativeFixture() => new(WorkbookFixtures.Authoritative);

        public static Fixture Legacy() => new(LegacyWorkbook);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* temp; best effort */ }
        }
    }

    // ---------------------------------------------------------------- request construction

    private static readonly Dictionary<string, string> DefaultValues = new(StringComparer.Ordinal)
    {
        ["LineageId"] = "LIN-W8D-0001",
        ["WorkId"] = "W8D",
        ["RepositoryId"] = "Platform",
        ["Branch"] = "w8d/v3-append",
        ["BaseSHA"] = "da8c546f4ddf4097d8f7ba7bb85511aeee6efedb",
        ["CommitSha"] = "0000000000000000000000000000000000000000",
    };

    /// <summary>
    /// A well-formed append request. Each test perturbs exactly ONE member of this, which is what
    /// makes the positive twin a control: the only difference between the refusal and the success is
    /// the condition under test.
    /// </summary>
    private static DevelopmentControlAppendRecord Request(
        string logicalSheet = "GitLineage",
        string identityColumn = "LineageId",
        string changeId = "CHG-W8D-APPEND-0001",
        Dictionary<string, string>? values = null,
        DevelopmentControlProvenance? provenance = null,
        ChangeScopeDeclaration? scope = null) =>
        new(logicalSheet,
            identityColumn,
            changeId,
            values ?? new Dictionary<string, string>(DefaultValues, StringComparer.Ordinal),
            provenance ?? DevelopmentControlProvenance.FromLegacy(
                "V2B", "NEXUS_DEVELOPMENT_CONTROL_20260905_workingtree-revision.xlsx",
                "6D42C3BF", "13_GitLineage", "L-0001", "r1", "V2",
                "carried into V3 by W8D", DateTimeOffset.Parse("2026-09-16T00:00:00Z")),
            scope ?? ScopeCovering());

    /// <summary>
    /// A declaration that DOES cover the store for write — the control baseline.
    ///
    /// <para><b>The change id is a parameter because a record's ChangeId and its scope's ChangeId
    /// must agree.</b> The governed writer refuses a record whose envelope names one change while
    /// its declaration authorises another — the record would be stamped with an origin that never
    /// authorised it — and it refuses that BEFORE containment and before any marker guard. The
    /// probes that use a change id of their own (the GapB trio) therefore declare a scope for that
    /// same change, or they would be refused for the change id and would never reach the guard they
    /// exist to exercise. That is not hypothetical: it happened, and the two probes that assert only
    /// <c>Appended == false</c> kept passing on the wrong refusal. Matching the id here is what
    /// makes their refusal provably about the marker.</para>
    /// </summary>
    private static ChangeScopeDeclaration ScopeCovering(string changeId = "CHG-W8D-APPEND-0001") =>
        new("w8d-append-lane", changeId,
            [new ChangeScopeItem(ChangeScopeItemKind.ControlStore,
                ChangeScopeAccessMode.Write, "NEXUS_DEVELOPMENT_CONTROL.xlsx")]);

    private static IDevelopmentControlReservation Hold(Fixture fixture, string owner = "w8d-t3") =>
        new DevelopmentControlLockService().TryAcquire(fixture.Copy, owner: owner).Reservation
        ?? throw new InvalidOperationException("the fixture lock was not acquired");

    private static DevelopmentControlAppendResult Append(Fixture fixture, DevelopmentControlAppendRecord request)
    {
        using var reservation = Hold(fixture);
        return new DevelopmentControlWriterAuthorizer().Append(reservation, request);
    }

    // ================================================================ positive control

    /// <summary>
    /// The control the rest of this file depends on: a well-formed append to a governed V3 sheet
    /// succeeds, lands on the first free row, and reads back with its envelope intact.
    ///
    /// <para>If this test fails, every refusal below is meaningless — they would all be passing
    /// because append never works.</para>
    /// </summary>
    [Fact]
    public void A_WellFormedAppend_Succeeds_AndReadsBackWithItsEnvelope()
    {
        using var fixture = Fixture.Candidate();
        var before = new DevelopmentControlReader().Read(fixture.Copy);

        var lineageBefore = before.Sheet("GitLineage")!;
        Assert.True(lineageBefore.Present);
        Assert.Empty(lineageBefore.Records);

        var result = Append(fixture, Request());

        Assert.True(result.Appended, result.Reason);
        // 13_GitLineage carries header row 4 and no data, so the first record is row 5.
        Assert.Equal(5, result.Row!.Value);
        Assert.Equal("LIN-W8D-0001", result.RecordKey);

        var after = new DevelopmentControlReader().Read(fixture.Copy);
        var row = after.Sheet("GitLineage")!.Records.Single(r => r.Row == 5);

        // The business values round-tripped.
        Assert.Equal("LIN-W8D-0001", row.Get("LineageId"));
        Assert.Equal("w8d/v3-append", row.Get("Branch"));

        // The governance envelope was written by the component and is readable through the contract.
        var envelope = row.Envelope;
        Assert.NotNull(envelope);
        Assert.Equal("1", envelope!.RecordVersion);
        Assert.True(envelope.IsCurrent);
        Assert.Equal("CHG-W8D-APPEND-0001", envelope.ChangeId);
        Assert.Equal("", envelope.SupersedesVersion);

        // The migration envelope is the provenance the caller declared, not a blank.
        var provenance = row.Provenance;
        Assert.NotNull(provenance);
        Assert.Equal("V2B", provenance!.SourceForm);
        Assert.Equal("13_GitLineage", provenance.SourceSheet);
        Assert.Equal("L-0001", provenance.SourceRecordId);
        Assert.True(provenance.IsWellFormed(out _));

        // The write landed, so the bytes changed...
        Assert.NotEqual(before.Sha256, after.Sha256);
        // ...and it did NOT promote the authority. A write is not a cutover.
        Assert.Equal(DevelopmentControlAuthority.Candidate, after.Authority);

        Record($"T3.1 append GitLineage row 5 LineageId=LIN-W8D-0001 envelope=1/Yes/{envelope.ChangeId} "
             + $"provenance={provenance.SourceForm}/{provenance.SourceSheet}/{provenance.SourceRecordId} "
             + $"authority={after.Authority}");
    }

    // ================================================================ the nine refusals

    [Fact]
    public void Append_Refuses_TheLegacyFourteenSheetAuthority()
    {
        using var fixture = Fixture.Legacy();

        var result = Append(fixture, Request());

        Assert.False(result.Appended);
        Assert.Contains("may not be appended to", result.Reason, StringComparison.Ordinal);
        Record($"T3.2 legacy refused :: {result.Reason}");

        // Control: the SAME request against the governed V3 candidate succeeds. Without this the
        // refusal above would be consistent with "the request was malformed all along".
        using var candidate = Fixture.Candidate();
        Assert.True(Append(candidate, Request()).Appended);
    }

    [Fact]
    public void Append_Refuses_AnUnknownSheet()
    {
        using var fixture = Fixture.Candidate();
        var before = fixture.Sha256();

        var result = Append(fixture, Request(logicalSheet: "NoSuchSheet"));

        Assert.False(result.Appended);
        Assert.Contains("no readable sheet is bound", result.Reason, StringComparison.Ordinal);
        Assert.Contains("NoSuchSheet", result.Reason, StringComparison.Ordinal);
        // A refusal must not have written anything.
        Assert.Equal(before, fixture.Sha256());

        using var candidate = Fixture.Candidate();
        Assert.True(Append(candidate, Request(logicalSheet: "GitLineage")).Appended);
    }

    [Fact]
    public void Append_Refuses_AnUnknownColumn()
    {
        using var fixture = Fixture.Candidate();
        var before = fixture.Sha256();

        var values = new Dictionary<string, string>(DefaultValues, StringComparer.Ordinal)
        {
            ["NoSuchColumn"] = "x",
        };

        var result = Append(fixture, Request(values: values));

        Assert.False(result.Appended);
        Assert.Contains("no logical column 'NoSuchColumn'", result.Reason, StringComparison.Ordinal);
        Assert.Equal(before, fixture.Sha256());

        using var candidate = Fixture.Candidate();
        Assert.True(Append(candidate, Request()).Appended);
    }

    [Fact]
    public void Append_Refuses_AValuesMapWithoutTheDeclaredIdentityColumn()
    {
        using var fixture = Fixture.Candidate();

        var values = new Dictionary<string, string>(DefaultValues, StringComparer.Ordinal);
        values.Remove("LineageId");

        var result = Append(fixture, Request(values: values));

        Assert.False(result.Appended);
        Assert.Contains("identity column 'LineageId'", result.Reason, StringComparison.Ordinal);

        using var candidate = Fixture.Candidate();
        Assert.True(Append(candidate, Request()).Appended);
    }

    [Fact]
    public void Append_Refuses_A_DuplicateImmutableId()
    {
        using var fixture = Fixture.Candidate();

        // First append: legal.
        Assert.True(Append(fixture, Request()).Appended);

        // Second append with the SAME LineageId: a collision, not an update.
        var second = Append(fixture, Request(logicalSheet: "GitLineage"));

        Assert.False(second.Appended);
        Assert.Contains("already exists", second.Reason, StringComparison.Ordinal);
        Assert.Contains("LIN-W8D-0001", second.Reason, StringComparison.Ordinal);

        // The refusal left the workbook at one record — it did not add a second row.
        var read = new DevelopmentControlReader().Read(fixture.Copy);
        Assert.Single(read.Sheet("GitLineage")!.Records);

        // Control: the identical request with a DIFFERENT id succeeds, so the refusal is about the
        // collision and not about the second append being malformed in some other way.
        var values = new Dictionary<string, string>(DefaultValues, StringComparer.Ordinal)
        {
            ["LineageId"] = "LIN-W8D-0002",
        };
        Assert.True(Append(fixture, Request(values: values)).Appended);
    }

    [Fact]
    public void Append_Refuses_OutsideTheDeclaredChangeScope()
    {
        using var fixture = Fixture.Candidate();
        var before = fixture.Sha256();

        // A scope that declares no ControlStore item at all.
        var scope = new ChangeScopeDeclaration("w8d-append-lane", "CHG-W8D-APPEND-0001",
            [new ChangeScopeItem(ChangeScopeItemKind.ExactFile, ChangeScopeAccessMode.Write,
                "docs/notes.md")]);

        var result = Append(fixture, Request(scope: scope));

        Assert.False(result.Appended);
        Assert.Contains("declares no ControlStore item", result.Reason, StringComparison.Ordinal);
        Assert.Equal(ChangeScopeContainmentVerdict.ScopeAmendmentRequired, result.Containment);
        Assert.Equal(before, fixture.Sha256());

        using var candidate = Fixture.Candidate();
        Assert.True(Append(candidate, Request(scope: ScopeCovering())).Appended);
    }

    [Fact]
    public void Append_Refuses_A_ControlStoreDeclaredForReadOnly()
    {
        using var fixture = Fixture.Candidate();

        // The declaration mentions the store — but as a READ. A lane that has said it will not
        // modify the authority is not entitled to append to it.
        var scope = new ChangeScopeDeclaration("w8d-append-lane", "CHG-W8D-APPEND-0001",
            [new ChangeScopeItem(ChangeScopeItemKind.ControlStore,
                ChangeScopeAccessMode.Read, "NEXUS_DEVELOPMENT_CONTROL.xlsx")]);

        var result = Append(fixture, Request(scope: scope));

        Assert.False(result.Appended);
        Assert.Contains("READ, not", result.Reason, StringComparison.Ordinal);
        Assert.Equal(ChangeScopeContainmentVerdict.ScopeAmendmentRequired, result.Containment);

        using var candidate = Fixture.Candidate();
        Assert.True(Append(candidate, Request(scope: ScopeCovering())).Appended);
    }

    [Fact]
    public void Append_Refuses_A_ScopeNamingADifferentStore()
    {
        using var fixture = Fixture.Candidate();

        // The estate really does carry a file by this name — one of the two preserved 14-sheet
        // revisions. A scope naming IT must not authorise a write to the authority.
        var scope = new ChangeScopeDeclaration("w8d-append-lane", "CHG-W8D-APPEND-0001",
            [new ChangeScopeItem(ChangeScopeItemKind.ControlStore, ChangeScopeAccessMode.Write,
                "NEXUS_DEVELOPMENT_CONTROL_20260830_pre-cutover_backup.xlsx")]);

        var result = Append(fixture, Request(scope: scope));

        Assert.False(result.Appended);
        Assert.Contains("not THIS one", result.Reason, StringComparison.Ordinal);
        Assert.Equal(ChangeScopeContainmentVerdict.ScopeAmendmentRequired, result.Containment);

        using var candidate = Fixture.Candidate();
        Assert.True(Append(candidate, Request(scope: ScopeCovering())).Appended);
    }

    // ================================================================ TASK 5 / T-2
    //
    // W8D-R5 recorded a cutover blocker as "SCOPE_CHANGE_REQUIRED is absent". The condition was
    // enforced, but only as English prose, so a host could not branch on it. These two tests are
    // the negative and positive controls for the typed vocabulary that replaces the prose: the
    // value must be present, must be the same value for every cause that shares the decision, and
    // must NOT be reported for refusals that were reached before the question was ever asked.

    /// <summary>
    /// Every way a declaration can fail to cover the store yields the SAME verdict, because they
    /// share one decision, while the four reasons stay distinct — because they do not share a
    /// remedy. A single verdict with a collapsed reason would send every caller to the same wrong
    /// fix; distinct verdicts would invent a vocabulary no legacy artefact defines.
    /// </summary>
    [Fact]
    public void Append_Reports_ScopeAmendmentRequired_ForEveryWayADeclarationCanFailToCoverTheStore()
    {
        var causes = new (string Name, ChangeScopeDeclaration? Scope)[]
        {
            ("no declaration at all", null),
            ("no ControlStore item", new ChangeScopeDeclaration("w8d-append-lane", "CHG-W8D-APPEND-0001",
                [new ChangeScopeItem(ChangeScopeItemKind.ExactFile, ChangeScopeAccessMode.Write, "docs/notes.md")])),
            ("ControlStore declared for READ", new ChangeScopeDeclaration("w8d-append-lane", "CHG-W8D-APPEND-0001",
                [new ChangeScopeItem(ChangeScopeItemKind.ControlStore, ChangeScopeAccessMode.Read,
                    "NEXUS_DEVELOPMENT_CONTROL.xlsx")])),
            ("a different store named", new ChangeScopeDeclaration("w8d-append-lane", "CHG-W8D-APPEND-0001",
                [new ChangeScopeItem(ChangeScopeItemKind.ControlStore, ChangeScopeAccessMode.Write,
                    "NEXUS_DEVELOPMENT_CONTROL_20260830_pre-cutover_backup.xlsx")])),
        };

        var reasons = new List<string>();

        foreach (var (name, scope) in causes)
        {
            using var fixture = Fixture.Candidate();
            var before = fixture.Sha256();

            // `Request` substitutes the covering declaration for a null scope - which is what makes
            // it a useful default everywhere else, and what made this branch unreachable through it.
            // The null case has to be built by REMOVING the declaration after the default is applied,
            // or the test would assert about a declaration it never actually withheld. Measured: the
            // first run of this test appended successfully on this row for exactly that reason.
            var request = Request();
            if (scope is null) request = request with { DeclaredScope = null! };
            else request = request with { DeclaredScope = scope };

            var result = Append(fixture, request);

            Assert.False(result.Appended);
            Assert.Equal(ChangeScopeContainmentVerdict.ScopeAmendmentRequired, result.Containment);
            Assert.Equal(before, fixture.Sha256());
            reasons.Add(result.Reason);
            Record($"T5.T2 {name} :: {result.Containment} :: {result.Reason}");
        }

        // The four reasons must be four different sentences. If two causes shared one sentence the
        // verdict would be carrying information the reason was supposed to carry, and the collapse
        // above would be hiding a lost distinction rather than making a deliberate one.
        Assert.Equal(4, reasons.Distinct(StringComparer.Ordinal).Count());

        // The positive twin: the same call with a covering declaration still appends, so the four
        // refusals above are the scope and not an append path that stopped working.
        using var candidate = Fixture.Candidate();
        var covered = Append(candidate, Request(scope: ScopeCovering()));
        Assert.True(covered.Appended);
        Assert.Equal(ChangeScopeContainmentVerdict.WithinDeclaredScope, covered.Containment);
    }

    /// <summary>
    /// A refusal reached BEFORE the containment question was asked reports
    /// <see cref="ChangeScopeContainmentVerdict.NotEvaluated"/>, not
    /// <see cref="ChangeScopeContainmentVerdict.WithinDeclaredScope"/>. This is the assertion that
    /// stops the value defaulting its way to "your scope is fine" — a released reservation must
    /// never read as an approved scope.
    /// </summary>
    [Fact]
    public void Append_Reports_NotEvaluated_WhenItRefusedBeforeAskingAboutTheScope()
    {
        using var fixture = Fixture.Candidate();

        var service = new DevelopmentControlLockService();
        var reservation = service.TryAcquire(fixture.Copy, owner: "w8d-t5-t2").Reservation!;
        reservation.Dispose();

        var result = new DevelopmentControlWriterAuthorizer().Append(reservation, Request());

        Assert.False(result.Appended);
        Assert.Equal(ChangeScopeContainmentVerdict.NotEvaluated, result.Containment);
        Record($"T5.T2 pre-scope refusal reports {result.Containment} :: {result.Reason}");

        // And the post-scope refusals report the opposite, so the two are genuinely distinguished
        // rather than both happening to be the default.
        using var candidate = Fixture.Candidate();
        var postScope = Append(candidate, Request(identityColumn: "NotAColumnAtAll"));
        Assert.False(postScope.Appended);
        Assert.Equal(ChangeScopeContainmentVerdict.WithinDeclaredScope, postScope.Containment);
    }

    [Fact]
    public void Append_Refuses_WithoutAReservation()
    {
        using var fixture = Fixture.Candidate();
        var before = fixture.Sha256();

        var service = new DevelopmentControlLockService();
        var attempt = service.TryAcquire(fixture.Copy, owner: "w8d-t3-released");
        var reservation = attempt.Reservation!;

        // Released: this caller no longer excludes another writer.
        reservation.Dispose();
        Assert.False(reservation.Held);

        var result = new DevelopmentControlWriterAuthorizer().Append(reservation, Request());

        Assert.False(result.Appended);
        Assert.Contains("already been released", result.Reason, StringComparison.Ordinal);
        Assert.Equal(before, fixture.Sha256());

        using var candidate = Fixture.Candidate();
        Assert.True(Append(candidate, Request()).Appended);
    }

    [Fact]
    public void Append_Refuses_WhileAnotherWriterHoldsTheLock()
    {
        using var fixture = Fixture.Candidate();

        using var holder = Hold(fixture, "holder");

        // The contender cannot obtain a reservation at all — which is the property that makes the
        // single-writer guarantee hold: there is no handle to append with.
        var contender = new DevelopmentControlLockService()
            .TryAcquire(fixture.Copy, owner: "contender");

        Assert.NotEqual(DevelopmentControlLockOutcome.Acquired, contender.Outcome);
        Assert.Null(contender.Reservation);
        Record($"T3.3 contention :: outcome={contender.Outcome} reservation={(contender.Reservation is null ? "none" : "ISSUED")}");

        // Control: once the holder releases, the same contender DOES acquire — so the refusal above
        // is contention and not a broken lock service.
        holder.Dispose();
        var second = new DevelopmentControlLockService()
            .TryAcquire(fixture.Copy, owner: "contender");
        Assert.Equal(DevelopmentControlLockOutcome.Acquired, second.Outcome);
        Assert.NotNull(second.Reservation);
        second.Reservation!.Dispose();
    }

    [Fact]
    public void Append_Refuses_ToLetTheCallerSupplyTheGovernanceEnvelope()
    {
        using var fixture = Fixture.Candidate();
        var before = fixture.Sha256();

        // A caller able to set IsCurrent or the originating ChangeId could place a record anywhere
        // in the append-only trail it liked.
        var values = new Dictionary<string, string>(DefaultValues, StringComparer.Ordinal)
        {
            ["IsCurrent"] = "Yes",
        };

        var result = Append(fixture, Request(values: values));

        Assert.False(result.Appended);
        Assert.Contains("is an envelope column", result.Reason, StringComparison.Ordinal);
        Assert.Equal(before, fixture.Sha256());

        using var candidate = Fixture.Candidate();
        Assert.True(Append(candidate, Request()).Appended);
    }

    /// <summary>
    /// TASK 3 — "structure-changing operation not permitted by schema".
    ///
    /// <para><b>Corrected after this test failed for the wrong reason.</b> It first appended to a
    /// logical sheet named <c>Dashboard</c>, on the premise that <c>25_Dashboard</c> is reachable by
    /// name and refuses because it carries no envelope. It is not reachable: <c>25_Dashboard</c> has
    /// no logical binding at all. So the refusal it produced — "no readable sheet is bound" — was
    /// the right answer to a different question, and the assertion of the intended reason is what
    /// caught it.</para>
    ///
    /// <para><b>The premise is now asserted rather than assumed.</b> The reason a governed append
    /// cannot reach <c>25_Dashboard</c> is not that the envelope check rejects it; it is that the V3
    /// form does not PROJECT it, so there is no logical name to append under and the operation would
    /// require inventing one. That is what "not permitted by schema" means here, and the test states
    /// it as a checked fact: the sheet is on the unbound list, and no logical name resolves to
    /// it.</para>
    /// </summary>
    [Fact]
    public void Append_Refuses_ASheetTheSchemaDoesNotProject()
    {
        using var fixture = Fixture.Candidate();
        var before = fixture.Sha256();

        var read = new DevelopmentControlReader().Read(fixture.Copy);

        // The premise: 25_Dashboard is present in the container and NOT projected by any binding.
        Assert.Contains("25_Dashboard", read.UnboundSheets);
        Assert.DoesNotContain(read.Sheets, s =>
            string.Equals(s.PhysicalName, "25_Dashboard", StringComparison.OrdinalIgnoreCase));

        var result = Append(fixture, Request(logicalSheet: "25_Dashboard"));

        Assert.False(result.Appended);
        Assert.Contains("no readable sheet is bound", result.Reason, StringComparison.Ordinal);
        Assert.Equal(before, fixture.Sha256());
        Record($"T3.4 unprojected sheet refused :: {result.Reason}");

        using var candidate = Fixture.Candidate();
        Assert.True(Append(candidate, Request(logicalSheet: "GitLineage")).Appended);
    }

    /// <summary>
    /// TASK 1 — "the writer must NOT gain arbitrary sheet creation capability".
    ///
    /// <para><b>Asserted as package structure, because that is where sheet creation would show.</b>
    /// A writer that quietly added a sheet, a column, or a relationship would still satisfy every
    /// value-level assertion in this file — the appended record would read back perfectly. So this
    /// compares the container itself: the same number of parts, the same sheet names in the same
    /// order, and the same unbound-sheet list, before and after a successful append.</para>
    /// </summary>
    [Fact]
    public void Append_AddsARecordWithoutChangingThePackageStructure()
    {
        using var fixture = Fixture.Candidate();

        var beforeRead = new DevelopmentControlReader().Read(fixture.Copy);
        var beforeParts = PackageParts(fixture.Copy);
        var beforeSheets = SheetNames(fixture.Copy);

        Assert.True(Append(fixture, Request()).Appended);

        var afterRead = new DevelopmentControlReader().Read(fixture.Copy);
        var afterParts = PackageParts(fixture.Copy);
        var afterSheets = SheetNames(fixture.Copy);

        // No sheet was created, removed, renamed or reordered.
        Assert.Equal(beforeSheets, afterSheets);

        // No part was added or dropped. The only part whose BYTES changed is the target sheet.
        Assert.Equal(beforeParts.Keys.OrderBy(k => k, StringComparer.Ordinal),
                     afterParts.Keys.OrderBy(k => k, StringComparer.Ordinal));

        var changed = beforeParts.Keys
            .Where(k => beforeParts[k] != afterParts[k])
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        // W10.0A FINAL: the expected part is DERIVED from the workbook's own sheet order rather than
        // hardcoded. It used to read "xl/worksheets/sheet14.xml", which was true of the archived
        // revision this test happened to run against and is a property of that FILE, not of the
        // behaviour under test. The behaviour is: exactly one part changes, and it is the part the
        // append target belongs to.
        var appendTarget = SheetNames(fixture.Copy).ToList().IndexOf("13_GitLineage") + 1;
        Assert.True(appendTarget > 0, "the fixture must declare 13_GitLineage");
        Assert.Equal(new[] { $"xl/worksheets/sheet{appendTarget}.xml" }, changed);

        // The writer appends inline strings, so it must neither add a shared-string table nor alter one
        // it found. The fixture has none, so the assertion is written for both cases rather than
        // indexing a part that need not exist — an absent table that stays absent is the same claim.
        Assert.Equal(
            beforeParts.ContainsKey("xl/sharedStrings.xml"),
            afterParts.ContainsKey("xl/sharedStrings.xml"));

        if (beforeParts.ContainsKey("xl/sharedStrings.xml"))
        {
            Assert.Equal(beforeParts["xl/sharedStrings.xml"], afterParts["xl/sharedStrings.xml"]);
        }

        // And the unbound-sheet claim is unchanged, so nothing new escaped projection.
        Assert.Equal(beforeRead.UnboundSheets.OrderBy(x => x, StringComparer.Ordinal),
                     afterRead.UnboundSheets.OrderBy(x => x, StringComparer.Ordinal));

        Record($"T3.7 append changed exactly {changed.Length} part(s): {string.Join(", ", changed)}; "
             + $"sheets={afterSheets.Count}; SST byte-identical=True");
    }

    private static Dictionary<string, string> PackageParts(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            using var stream = entry.Open();
            using var sha = System.Security.Cryptography.SHA256.Create();
            result[entry.FullName] = Convert.ToHexString(sha.ComputeHash(stream));
        }

        return result;
    }

    private static List<string> SheetNames(string path)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var entry = zip.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        var doc = System.Xml.Linq.XDocument.Load(stream);
        System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return doc.Root!.Element(ns + "sheets")!.Elements(ns + "sheet")
            .Select(s => (string)s.Attribute("name")!)
            .ToList();
    }

    [Fact]
    public void Append_Refuses_ProvenanceThatNamesALegacySourceItIsNotFrom()
    {
        using var fixture = Fixture.Candidate();

        // A record created in V3 has no legacy cell to point at. Naming one directs a future reader
        // to a cell that does not carry the record they are holding.
        var native = new DevelopmentControlProvenance(
            DevelopmentControlProvenance.NativeSourceForm,
            "NEXUS_DEVELOPMENT_CONTROL_20260905_workingtree-revision.xlsx",
            "", "13_GitLineage", "L-0001", "", "", "", "created in V3");

        var result = Append(fixture, Request(provenance: native));

        Assert.False(result.Appended);
        Assert.Contains("provenance is not well-formed", result.Reason, StringComparison.Ordinal);

        // Control: the same shape with the legacy fields cleared IS well-formed and appends.
        using var candidate = Fixture.Candidate();
        Assert.True(Append(candidate, Request(provenance: DevelopmentControlProvenance.Native(
            DateTimeOffset.Parse("2026-09-16T00:00:00Z"), "created in V3 by W8D append proof"))).Appended);
    }

    // ================================================================ the authority marker

    /// <summary>
    /// TASK 3 — "authority-marker mutation".
    ///
    /// <para><b>What this test used to assert, and why that was wrong.</b> Until W8D FINAL this test
    /// proved the marker reading was sensitive by POINTING THE CELL WRITER AT THE MARKER and showing
    /// it moved — <c>Assert.True(write.Written)</c> on a write of <c>AUTHORITATIVE</c> into
    /// <c>Control.Value</c> at row 10. The mutation control was real, but it was built on the defect:
    /// it certified that an ordinary cell write could promote the authority, which is exactly what
    /// the directive forbids. The control has been kept and re-sited — the CUTOVER now proves the
    /// sensor moves (see <see cref="Cutover_PromotesEverySiteAtomically_AndProvesTheSensorMoves"/>),
    /// and the ordinary paths are asserted to be unable to reach the cell at all.</para>
    ///
    /// <para><b>Both halves are still needed.</b> "The marker did not change" passes trivially if
    /// the assertion is broken, the wrong cell is read, or the fixture is stale. The sensitivity
    /// proof lives in the cutover test; this test asserts the refusals, and the pair together is
    /// the mutation control.</para>
    /// </summary>
    [Fact]
    public void NoOrdinaryWrite_CanPromoteTheAuthority()
    {
        // ---- Half 1: the cell writer refuses the marker cell, and says why, and leaves the file alone.
        using (var probe = Fixture.Candidate())
        {
            var beforeMarker = WorkbookCompatibilityReader.AuthorityMarker(
                WorkbookCompatibilityReader.Read(probe.Copy));
            var beforeSha = probe.Sha256();
            Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerCandidate, beforeMarker);

            using var reservation = Hold(probe, "w8d-t3-marker-probe");
            var write = new DevelopmentControlWriterAuthorizer().Write(
                reservation,
                new DevelopmentControlCellWrite("Control", 10, "Value",
                    WorkbookCompatibilityReader.AuthorityMarkerAuthoritative));

            Assert.False(write.Written, write.Reason);
            Assert.Equal(DevelopmentControlWriteVerdict.RefusedAuthoritySite, write.Verdict);
            Assert.Contains("GOVERNED_CELL", write.Reason, StringComparison.Ordinal);

            // Not merely "reported as refused": the bytes are untouched. A guard that refused in
            // its return value while still writing would pass every assertion above except this one.
            Assert.Equal(beforeSha, probe.Sha256());

            var afterMarker = WorkbookCompatibilityReader.AuthorityMarker(
                WorkbookCompatibilityReader.Read(probe.Copy));
            Assert.Equal(beforeMarker, afterMarker);

            Record($"T3.5 marker write refused via the cell path :: verdict={write.Verdict}; "
                 + $"sha unchanged={beforeSha[..12]}…; marker still {afterMarker}");
        }

        // ---- Half 1b: the same refusal through the OTHER logically reachable site, so the guard is
        // proved to be a property of the SITES rather than of one hardcoded cell address.
        using (var probe = Fixture.Candidate())
        {
            using var reservation = Hold(probe, "w8d-t3-marker-probe-2");
            var write = new DevelopmentControlWriterAuthorizer().Write(
                reservation,
                new DevelopmentControlCellWrite("MigrationMap", 16, "Status",
                    WorkbookCompatibilityReader.AuthorityMarkerAuthoritative));

            Assert.False(write.Written, write.Reason);
            Assert.Equal(DevelopmentControlWriteVerdict.RefusedAuthoritySite, write.Verdict);

            Record($"T3.5b marker write refused via the migration-map site :: verdict={write.Verdict}");
        }

        // ---- Half 2: append cannot reach it, on a fresh copy.
        using (var fixture = Fixture.Candidate())
        {
            var beforeRead = WorkbookCompatibilityReader.Read(fixture.Copy);
            var beforeMarker = WorkbookCompatibilityReader.AuthorityMarker(beforeRead);

            // A caller trying to smuggle the marker in as a value: the envelope guard refuses
            // because the marker's item name is not a column of GitLineage.
            var smuggling = new Dictionary<string, string>(DefaultValues, StringComparer.Ordinal)
            {
                ["ControlState"] = "AUTHORITATIVE",
            };
            Assert.False(Append(fixture, Request(values: smuggling)).Appended);

            // A well-formed append, which does succeed.
            Assert.True(Append(fixture, Request()).Appended);

            var afterRead = WorkbookCompatibilityReader.Read(fixture.Copy);
            var afterMarker = WorkbookCompatibilityReader.AuthorityMarker(afterRead);

            Assert.Equal(beforeMarker, afterMarker);
            Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerCandidate, afterMarker);
            Assert.Equal(DevelopmentControlAuthority.Candidate,
                new DevelopmentControlReader().Read(fixture.Copy).Authority);

            Record($"T3.6 marker unchanged by append :: {beforeMarker} -> {afterMarker}; "
                 + $"authority={new DevelopmentControlReader().Read(fixture.Copy).Authority}");
        }
    }

    /// <summary>
    /// The sites are declared ONCE, in Contracts, and this is the assertion that keeps the
    /// declaration honest. Two facts are load-bearing and both are easy to lose silently:
    /// <list type="number">
    /// <item><description>There are exactly FOUR sites. A fifth added to the model but not to the
    /// list would be promotable by an ordinary write and nobody would find out until it
    /// happened.</description></item>
    /// <item><description>Exactly TWO are reachable by a logical write. If a future binding makes
    /// <c>01_Configuration</c> or <c>25_Dashboard</c> addressable, the logical guard starts
    /// covering them and the count here changes — which is the moment a human should look, not a
    /// silent widening of the guarded surface.</description></item>
    /// </list>
    /// </summary>
    [Fact]
    public void TheAuthoritySites_AreDeclared_AndTheirReachabilityIsExplicit()
    {
        Assert.Equal(4, DevelopmentControlAuthoritySites.All.Count);

        Assert.Equal(new[] { "00_Control!B10", "01_Configuration!D16", "24_V3MigrationMap!R16", "25_Dashboard!B5" },
            DevelopmentControlAuthoritySites.All.Select(s => s.Reference).ToArray());

        Assert.Equal(new[] { "Control!Value@10", "MigrationMap!Status@16" },
            DevelopmentControlAuthoritySites.ReachableByLogicalWrite
                .Select(s => $"{s.LogicalSheet}!{s.LogicalColumn}@{s.Row}").ToArray());

        // The physical predicate is what the writer enforces, so it is checked directly rather than
        // only through a write that happens to exercise it.
        Assert.NotNull(DevelopmentControlAuthoritySites.FindPhysical("00_Control", "B", 10));
        Assert.NotNull(DevelopmentControlAuthoritySites.FindPhysical("25_Dashboard", "b", 5)); // case-insensitive by design
        Assert.Null(DevelopmentControlAuthoritySites.FindPhysical("00_Control", "B", 11));
        Assert.Null(DevelopmentControlAuthoritySites.FindPhysical("00_Control", "C", 10));

        Record($"T3.7 authority sites declared :: {DevelopmentControlAuthoritySites.All.Count} total, "
             + $"{DevelopmentControlAuthoritySites.ReachableByLogicalWrite.Count} logically reachable");
    }

    // ================================================================ W8D FINAL — the cutover

    private static DevelopmentControlCutoverRequest CutoverRequest(
        bool suitesGreen = true,
        bool hostsAgree = true,
        string evidenceRef = "W8D_FINAL/_evidence/tests/w1-harness-final.txt",
        string changeId = "CHG-W8D-CUTOVER-0001") =>
        new(changeId,
            "W8D FINAL TASK 3: promote the reconciled 26-sheet V3 candidate to authoritative under "
            + "Owner decisions H-1A..H-1E, preserving both source states and recording the remaining "
            + "unresolved records explicitly.",
            new DevelopmentControlCutoverAttestation(suitesGreen, hostsAgree, evidenceRef));

    private static DevelopmentControlCutoverResult Cutover(
        Fixture fixture, DevelopmentControlCutoverRequest request)
    {
        using var reservation = Hold(fixture, "w8d-t3-cutover");
        return new DevelopmentControlWriterAuthorizer().Cutover(reservation, request);
    }

    /// <summary>
    /// TASK 3's positive path: the cutover promotes every site, in one atomic swap, and read-back
    /// through the canonical reader confirms it.
    ///
    /// <para><b>This test is also the marker sensor's sensitivity proof.</b> It shows the reading
    /// DOES move when the cell is written — by the one governed operation permitted to write it.
    /// Without that, every "the marker did not change" assertion elsewhere in this file would be
    /// measuring nothing.</para>
    /// </summary>
    [Fact]
    public void Cutover_PromotesEverySiteAtomically_AndProvesTheSensorMoves()
    {
        using var fixture = Fixture.Candidate();

        var beforeMarker = WorkbookCompatibilityReader.AuthorityMarker(
            WorkbookCompatibilityReader.Read(fixture.Copy));
        var beforeSha = fixture.Sha256();
        Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerCandidate, beforeMarker);

        var result = Cutover(fixture, CutoverRequest());

        Assert.True(result.Performed, result.Reason);
        Assert.Equal(DevelopmentControlCutoverVerdict.Performed, result.Verdict);

        // Every declared site moved, and the result NAMES each one rather than counting them.
        Assert.Equal(DevelopmentControlAuthoritySites.All.Count, result.Sites.Count);
        Assert.All(result.Sites, s =>
        {
            Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerCandidate, s.Before);
            Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerAuthoritative, s.After);
        });
        Assert.Contains("00_Control!B10:CANDIDATE->AUTHORITATIVE", result.SiteSummary, StringComparison.Ordinal);
        Assert.Contains("25_Dashboard!B5:CANDIDATE->AUTHORITATIVE", result.SiteSummary, StringComparison.Ordinal);

        // The bytes changed, and the result carries both ends so "which bytes are authoritative" is
        // answerable from the result alone.
        var afterSha = fixture.Sha256();
        Assert.Equal(beforeSha, result.Sha256Before);
        Assert.Equal(afterSha, result.Sha256After);
        Assert.NotEqual(beforeSha, afterSha);

        // The sensor moves: this is the sensitivity proof the old cell-write half used to provide.
        var afterMarker = WorkbookCompatibilityReader.AuthorityMarker(
            WorkbookCompatibilityReader.Read(fixture.Copy));
        Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerAuthoritative, afterMarker);
        Assert.NotEqual(beforeMarker, afterMarker);

        // And it moved for the READER both hosts use, not merely in the file.
        Assert.Equal(DevelopmentControlAuthority.Authoritative,
            new DevelopmentControlReader().Read(fixture.Copy).Authority);

        Record($"T3.8 cutover performed :: {beforeSha[..12]}… -> {afterSha[..12]}…; {result.SiteSummary}; "
             + $"unresolved={result.UnresolvedDecisionCount}; integrity=[{result.StructuralIntegrityReport}]");
    }

    /// <summary>
    /// H-1E's two conditions are the ones this component CANNOT test, so they must be stated. This
    /// test proves an unstated precondition is a typed refusal rather than a default — the exact
    /// failure mode of a "permissive by omission" gate.
    ///
    /// <para>The third case is the one worth having: a well-formed attestation carrying a BLANK
    /// evidence reference. Both booleans true and nothing to point at is an assertion without
    /// evidence, and it is refused for the same reason a claim is.</para>
    /// </summary>
    [Fact]
    public void Cutover_RefusesAnIncompleteAttestation()
    {
        foreach (var (label, request) in new[]
        {
            ("suites not green", CutoverRequest(suitesGreen: false)),
            ("hosts do not agree", CutoverRequest(hostsAgree: false)),
            ("no evidence reference", CutoverRequest(evidenceRef: "")),
        })
        {
            using var fixture = Fixture.Candidate();
            var beforeSha = fixture.Sha256();

            var result = Cutover(fixture, request);

            Assert.False(result.Performed);
            Assert.Equal(DevelopmentControlCutoverVerdict.RefusedAttestationIncomplete, result.Verdict);
            Assert.Equal(beforeSha, fixture.Sha256());

            Record($"T3.9 cutover refused ({label}) :: {result.Verdict}");
        }

        // Control: the identical request with the condition restored DOES perform, so the refusals
        // above are measuring the attestation and not the fixture.
        using var control = Fixture.Candidate();
        Assert.True(Cutover(control, CutoverRequest()).Performed);
    }

    /// <summary>
    /// A second cutover on an already-authoritative workbook is a no-op, not a second promotion.
    /// A re-run of a governed operation must be safe, and "already done" is not "failed".
    /// </summary>
    [Fact]
    public void Cutover_IsIdempotent()
    {
        using var fixture = Fixture.Candidate();

        Assert.True(Cutover(fixture, CutoverRequest()).Performed);

        var afterFirst = fixture.Sha256();

        var second = Cutover(fixture, CutoverRequest());

        Assert.False(second.Performed);
        Assert.Equal(DevelopmentControlCutoverVerdict.RefusedAlreadyAuthoritative, second.Verdict);
        Assert.Equal(afterFirst, fixture.Sha256());
        Assert.Equal(DevelopmentControlAuthority.Authoritative,
            new DevelopmentControlReader().Read(fixture.Copy).Authority);

        Record($"T3.10 cutover idempotent :: second run {second.Verdict}; sha unchanged={afterFirst[..12]}…");
    }

    /// <summary>
    /// The preserved 14-sheet revision cannot be promoted. This is H-1A's "legacy must remain
    /// historical provenance" expressed as an executable check: the cutover may not be used to
    /// convert a frozen revision into the current authority.
    ///
    /// <para>The refusal is <see cref="DevelopmentControlCutoverVerdict.RefusedNotV3"/> rather than
    /// a path refusal, and that is the correct verdict: the legacy workbook is refused because it
    /// is not the V3 model, which is a stronger reason than where the file happens to live.</para>
    /// </summary>
    [Fact]
    public void Cutover_RefusesThePreservedLegacyRevision()
    {
        using var fixture = Fixture.Legacy();
        var beforeSha = fixture.Sha256();

        var result = Cutover(fixture, CutoverRequest());

        Assert.False(result.Performed);
        Assert.Equal(DevelopmentControlCutoverVerdict.RefusedNotV3, result.Verdict);
        Assert.Equal(beforeSha, fixture.Sha256());

        // The legacy revision keeps its own authority designation, whatever that is — the cutover
        // did not overwrite it with the V3 vocabulary.
        var legacyMarker = WorkbookCompatibilityReader.AuthorityMarker(
            WorkbookCompatibilityReader.Read(fixture.Copy));

        Record($"T3.11 cutover refused on the preserved legacy revision :: {result.Verdict}; "
             + $"marker left as '{legacyMarker ?? "<unreadable>"}'");
    }

    /// <summary>
    /// The unresolved-decision count is MEASURED from the workbook being promoted, not accepted
    /// from the caller. H-1E permits an authoritative control model to contain explicit
    /// <c>HUMAN_DECISION_REQUIRED</c> records; this asserts the count reported at the moment of
    /// promotion agrees with an independent read of <c>20_Decisions</c>.
    ///
    /// <para><b>Why the "greater than zero" half is conditional, and on what.</b> An unconditional
    /// <c>expected &gt; 0</c> would be a claim about a particular artifact dressed up as a claim
    /// about the measurement — and it is false for the live authority, which predates the
    /// reconciliation and carries NONE of the four W8D decision records. Asserting it would force
    /// either a false pass or a false failure depending on which workbook the suite was pointed at.
    /// So the sensitivity check is tied to evidence INSIDE the workbook: if this workbook carries
    /// W8D reconciliation records at all, then it must also carry the explicit unresolved decisions
    /// those reconciliations recorded, because a count of zero there would mean the measurement is
    /// reading the wrong column rather than that nothing remains open.</para>
    /// </summary>
    [Fact]
    public void Cutover_MeasuresTheUnresolvedDecisionCount_FromTheWorkbookItself()
    {
        using var fixture = Fixture.Candidate();

        var decisions = WorkbookCompatibilityReader.Read(fixture.Copy).Sheet("Decisions")!;

        var expected = decisions.Records
            .Count(r => (r.Get("Decision") ?? "").TrimStart()
                .StartsWith("HUMAN_DECISION_REQUIRED", StringComparison.OrdinalIgnoreCase));

        // Does this workbook carry the W8D reconciliation at all? CHG-W8D-* is the ChangeId the
        // reconciliation wrote, so its presence is the evidence that the decision records for these
        // reconciliations should also be here.
        var reconciled = decisions.Records.Count(r =>
            (r.Get("ChangeId") ?? "").StartsWith("CHG-W8D", StringComparison.OrdinalIgnoreCase));

        var result = Cutover(fixture, CutoverRequest());

        Assert.True(result.Performed, result.Reason);
        Assert.Equal(expected, result.UnresolvedDecisionCount);

        if (reconciled > 0)
        {
            Assert.True(expected > 0,
                $"this workbook carries {reconciled} W8D reconciliation record(s), so it must also "
                + "carry the explicit unresolved decisions those reconciliations recorded. A count of "
                + "zero means the measurement is reading the wrong column, not that nothing is open.");
        }
        else
        {
            Record("T3.12 NOTE :: this workbook carries NO W8D reconciliation records, so it is the "
                 + "pre-reconciliation authority. It is expected to carry 0 unresolved decisions — "
                 + "the four W8D decision records exist only in the reconciled candidate, which is "
                 + "why TASK 3 cuts over the CANDIDATE and not this file.");
        }

        Record($"T3.12 unresolved decision count measured at promotion :: {result.UnresolvedDecisionCount} "
             + $"of {decisions.Records.Count} decision records carry HUMAN_DECISION_REQUIRED; "
             + $"W8D reconciliation records present={reconciled}");
    }

    /// <summary>
    /// The post-cutover sensor: the LIVE canonical workbook is the authority, and it reads as one.
    ///
    /// <para>Every other test in this file runs against a copy of an archived revision, so before this
    /// test existed nothing here observed the authority itself — the suite could stay green while the
    /// live workbook was demoted. This is the one assertion in the file that reads the canonical path,
    /// and it is deliberately semantic rather than hash-pinned: an ordinary governed write changes the
    /// bytes and must NOT fail this test, whereas a demotion must. (The Developer suite pins the hash
    /// instead, on purpose, as a staleness detector — see <c>FixtureWriteProofTests</c> there.)</para>
    /// </summary>
    // W10.0A FINAL: this test asserts a property of the LIVE estate file, so it carries the EstateHost
    // lane trait and runs in the estate lane (TASK 7B). Its portable twin, immediately below, proves
    // the same contract behaviour against a generated workbook — so the component's ability to read an
    // authoritative V3 workbook is still proved on every Linux CI run, and nothing is silently skipped.
    [Fact]
    [Trait("Lane", "EstateHost")]
    public void TheLiveCanonicalWorkbook_IsTheAuthority_AndReadsAsAuthoritative()
    {
        using var fixture = Fixture.LiveAuthority();

        var read = new DevelopmentControlReader().Read(fixture.Copy);

        Assert.Equal(DevelopmentControlForm.V3, read.Form);
        Assert.Equal(DevelopmentControlAuthority.Authoritative, read.Authority);

        Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerAuthoritative,
            WorkbookCompatibilityReader.AuthorityMarker(
                WorkbookCompatibilityReader.Read(fixture.Copy)));

        Record($"T3.13 live canonical authority :: form={read.Form} authority={read.Authority} "
             + $"sha={fixture.Sha256()[..12]}...");
    }

    /// <summary>
    /// <b>The portable twin of the test above.</b> Same form, same authority, same marker — against a
    /// workbook this process generated, so it runs on any host.
    ///
    /// <para>
    /// This is what TASK 3 asks for when a control is split: <i>"preserve equivalent platform-neutral
    /// contract proof in normal CI."</i> The estate test above answers <b>"is the live file still the
    /// authority?"</b>; this one answers <b>"does the component read an authoritative V3 workbook as
    /// authoritative?"</b>. They are different questions, and the second one must not stop being asked
    /// on the machine that cannot answer the first.
    /// </para>
    /// </summary>
    [Fact]
    public void AnAuthoritativeV3Workbook_ReadsAsAuthoritative()
    {
        using var fixture = Fixture.AuthoritativeFixture();

        var read = new DevelopmentControlReader().Read(fixture.Copy);

        Assert.Equal(DevelopmentControlForm.V3, read.Form);
        Assert.Equal(DevelopmentControlAuthority.Authoritative, read.Authority);

        Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerAuthoritative,
            WorkbookCompatibilityReader.AuthorityMarker(
                WorkbookCompatibilityReader.Read(fixture.Copy)));
    }

    // ================================================================ W8D PRODUCTION WIRING — marker-safety probe
    //
    // A read-only analysis of this writer produced two STATIC hypotheses about whether an ordinary
    // governed write can move what the authority marker resolves to. Neither had been executed, and
    // a hypothesis reasoned from code is not evidence. Each is probed below against a TEMP COPY of
    // the preserved pre-cutover candidate, asserting the SAFE outcome — so if the gap is real the
    // test FAILS and carries the measurement that decides it in its own failure message.
    //
    // Nothing here writes to the live authority: `Fixture.Candidate()` copies W1_V3_CANDIDATE into
    // its own throwaway directory first, exactly as every other test in this file does.
    //
    // The two hypotheses, and the guards each is claimed to escape:
    //
    //   Gap A — the marker's IDENTITY cell is unguarded. The marker is resolved by scanning
    //   `00_Control` for the row whose ControlItem column equals "ControlState" and reading that
    //   row's Value. The guards protect the VALUE cell, physically (`DevelopmentControlCellWriter`
    //   refuses the declared site `00_Control!B10`) and logically (`DevelopmentControlAuthoritySites
    //   .FindLogical("Control", 10, "Value")`). ControlItem is itself a bound, writable column at
    //   physical A10, and is NEITHER. Same shape at `24_V3MigrationMap!A16` for the MigrationMap site.
    //
    //   Gap B — ControlItems is LAST-WINS over records in ascending row order, and append to the
    //   logical `Control` sheet is claimed not to be blocked. A row appended BELOW row 10 declaring
    //   ControlItem = "ControlState" would shadow the real marker. If real this is the serious one:
    //   it is both promotion and demotion of the resolved marker by an ordinary governed write.

    /// <summary>
    /// The value Gap B tries to shadow the marker with. `AUTHORITATIVE` is chosen deliberately: it
    /// is the PROMOTION case, which is the worst outcome of the two and the one the directive's
    /// "normal governed writes must not change the marker" rule most directly forbids. The premise
    /// assertions in the probe confirm it is not already present in `00_Control` column B, so the
    /// identity-collision gate has nothing to refuse.
    /// </summary>
    private const string ShadowMarkerValue = WorkbookCompatibilityReader.AuthorityMarkerAuthoritative;

    /// <summary>
    /// Every row of the logical `Control` sheet whose <c>ControlItem</c> names the authority marker,
    /// with the value that row carries. More than one row is the shadowing condition; which of them
    /// wins is decided by row order, because <see cref="WorkbookCompatibilityReader.ControlItems"/>
    /// assigns into a plain dictionary as it walks the records ascending.
    /// </summary>
    private static string[] MarkerRows(WorkbookReadResult read) =>
        read.Sheet("Control")!.Records
            .Where(r => string.Equals((r.Get("ControlItem") ?? "").Trim(),
                WorkbookCompatibilityReader.AuthorityMarkerItem, StringComparison.Ordinal))
            .Select(r => $"row {r.Row} => '{r.Get("Value")}'")
            .ToArray();

    /// <summary>
    /// GAP A, at the site that decides: <c>00_Control!A10</c>, the marker's identity cell.
    ///
    /// <para><b>Asserts the SAFE outcome.</b> A10 is not a declared authority site at either layer,
    /// so on the static reading the write passes both guards and renames the item — after which
    /// <c>ControlItems</c> carries no "ControlState" key, <c>AuthorityMarker</c> returns null,
    /// <c>AuthorizeWrite</c> refuses with <c>RefusedAuthorityMarkerUnreadable</c> and the resolver
    /// reports <c>Unknown</c>. That is degradation rather than promotion, but it is still an ordinary
    /// governed write moving what the marker resolves to, which the directive forbids.</para>
    ///
    /// <para>The premise facts are asserted rather than assumed, because a refusal produced for a
    /// different reason would otherwise read as a pass. The failure message carries the A10 value,
    /// the marker and the resolved authority before and after, so the verdict is decided by the
    /// measurement and not by this comment.</para>
    /// </summary>
    [Fact]
    public void GapA_AnOrdinaryCellWrite_CannotRenameTheMarkersIdentityCell()
    {
        using var fixture = Fixture.Candidate();

        var beforeRead = WorkbookCompatibilityReader.Read(fixture.Copy);
        var beforeSha = fixture.Sha256();
        var markerBefore = WorkbookCompatibilityReader.AuthorityMarker(beforeRead);
        var identityBefore = beforeRead.Sheet("Control")!.Records.Single(r => r.Row == 10).Get("ControlItem");

        // --- the premise: A10 is the identity cell, it is NOT declared an authority site at either
        // layer, and it is the thing that makes the marker resolvable at all.
        Assert.Equal("ControlState", identityBefore);
        Assert.Null(DevelopmentControlAuthoritySites.FindPhysical("00_Control", "A", 10));
        Assert.Null(DevelopmentControlAuthoritySites.FindLogical("Control", 10, "ControlItem"));
        Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerCandidate, markerBefore);

        using var reservation = Hold(fixture, "w8d-gapA-identity");
        var write = new DevelopmentControlWriterAuthorizer().Write(
            reservation,
            new DevelopmentControlCellWrite("Control", 10, "ControlItem", "ControlStateRenamed"));

        var afterRead = WorkbookCompatibilityReader.Read(fixture.Copy);
        var afterSha = fixture.Sha256();
        var markerAfter = WorkbookCompatibilityReader.AuthorityMarker(afterRead);
        var identityAfter = afterRead.Sheet("Control")!.Records
            .FirstOrDefault(r => r.Row == 10)?.Get("ControlItem");

        var evidence =
            $"GapA/Control probing Write(Control, row 10, ControlItem) :: "
            + $"written={write.Written}; verdict={write.Verdict}; reason={write.Reason} || "
            + $"A10 ControlItem '{identityBefore}' -> '{identityAfter}'; "
            + $"marker '{markerBefore}' -> '{markerAfter ?? "<UNREADABLE>"}'; "
            + $"resolved authority={new DevelopmentControlReader().Read(fixture.Copy).Authority}; "
            + $"AuthorizeWrite afterwards="
            + $"{WorkbookCompatibilityReader.AuthorizeWrite(afterRead, WorkbookForm.V3).Verdict}; "
            + $"sha {beforeSha[..12]} -> {afterSha[..12]} "
            + $"unchanged={string.Equals(beforeSha, afterSha, StringComparison.OrdinalIgnoreCase)}";
        Record(evidence);

        // --- THE SAFE OUTCOME. If the identity cell is unguarded, this is what fails.
        Assert.False(write.Written, evidence);
        Assert.Equal(DevelopmentControlWriteVerdict.RefusedAuthoritySite, write.Verdict);
        Assert.Equal(beforeSha, afterSha);
        Assert.Equal("ControlState", identityAfter);
        Assert.Equal(markerBefore, markerAfter);
    }

    /// <summary>
    /// GAP A at the second site the same shape was reported at: <c>24_V3MigrationMap!A16</c>, the
    /// <c>MapId</c> cell of the row carrying the migration-status authority site at R16.
    ///
    /// <para>Kept separate from the Control probe because the two are different sheets with
    /// different bindings: the Control identity cell is reachable and refused for one reason, and
    /// this one establishes that the hole — if it is one — is a property of the SHAPE rather than of
    /// one hardcoded address. Renaming A16 does not move the resolved marker, so the assertion here
    /// is about the write being refused at all.</para>
    /// </summary>
    [Fact]
    public void GapA_AnOrdinaryCellWrite_CannotRenameTheMigrationMapSitesIdentityCell()
    {
        using var fixture = Fixture.Candidate();

        var beforeRead = WorkbookCompatibilityReader.Read(fixture.Copy);
        var beforeSha = fixture.Sha256();

        var row = beforeRead.Sheet("MigrationMap")!.Records.Single(r => r.Row == 16);
        var identityBefore = row.Get("MapId");

        // --- the premise: A16 is the row's key cell and is not a declared authority site at either
        // layer, while the Value-bearing R16 of the same row IS.
        Assert.Equal("MAP-unified-control", identityBefore);
        Assert.Null(DevelopmentControlAuthoritySites.FindPhysical("24_V3MigrationMap", "A", 16));
        Assert.Null(DevelopmentControlAuthoritySites.FindLogical("MigrationMap", 16, "MapId"));
        Assert.NotNull(DevelopmentControlAuthoritySites.FindPhysical("24_V3MigrationMap", "R", 16));
        Assert.NotNull(DevelopmentControlAuthoritySites.FindLogical("MigrationMap", 16, "Status"));

        using var reservation = Hold(fixture, "w8d-gapA-migrationmap-identity");
        var write = new DevelopmentControlWriterAuthorizer().Write(
            reservation,
            new DevelopmentControlCellWrite("MigrationMap", 16, "MapId", "MAP-unified-control-renamed"));

        var afterRead = WorkbookCompatibilityReader.Read(fixture.Copy);
        var afterSha = fixture.Sha256();
        var identityAfter = afterRead.Sheet("MigrationMap")!.Records
            .FirstOrDefault(r => r.Row == 16)?.Get("MapId");
        var statusAfter = afterRead.Sheet("MigrationMap")!.Records
            .FirstOrDefault(r => r.Row == 16)?.Get("Status");

        var evidence =
            $"GapA/MigrationMap probing Write(MigrationMap, row 16, MapId) :: "
            + $"written={write.Written}; verdict={write.Verdict}; reason={write.Reason} || "
            + $"A16 MapId '{identityBefore}' -> '{identityAfter}'; R16 Status left as '{statusAfter}'; "
            + $"sha {beforeSha[..12]} -> {afterSha[..12]} "
            + $"unchanged={string.Equals(beforeSha, afterSha, StringComparison.OrdinalIgnoreCase)}";
        Record(evidence);

        Assert.False(write.Written, evidence);
        Assert.Equal(DevelopmentControlWriteVerdict.RefusedAuthoritySite, write.Verdict);
        Assert.Equal(beforeSha, afterSha);
        Assert.Equal(identityBefore, identityAfter);
    }

    /// <summary>
    /// GAP B — the shadowing append.
    ///
    /// <para><b>Asserts the SAFE outcome.</b> On the static reading every gate passes: the workbook
    /// authorises a V3 write, `Control` is bound, `00_Control` row 4 carries the full 14-column
    /// envelope so the envelope gate has nothing to refuse, `ControlItem` and `Value` are real
    /// columns, `IdentityColumn` is caller-supplied and declared as <c>Value</c> with a unique
    /// value, and the scope is self-declared so it covers. The row then lands BELOW row 10, and
    /// `ControlItems` — last-wins over ascending rows — resolves the marker to the appended value.
    /// That is a genuine hole in the marker-safety guarantee: ordinary promotion of the authority,
    /// and by symmetry an ordinary demotion of it.</para>
    ///
    /// <para><b>Every gate above is asserted as a premise</b> so that a refusal for a different
    /// reason cannot be mistaken for the guard working. The companion test
    /// <see cref="GapB_AnAppendToTheControlSheetWithABenignItem_ReachesTheSheet"/> is the mutation
    /// control: it proves the append path does reach this sheet, so a refusal here is about the
    /// marker and not about an append that never worked.</para>
    /// </summary>
    [Fact]
    public void GapB_AnOrdinaryAppend_CannotShadowTheAuthorityMarkerOnTheControlSheet()
    {
        using var fixture = Fixture.Candidate();

        var beforeRead = WorkbookCompatibilityReader.Read(fixture.Copy);
        var beforeSha = fixture.Sha256();
        var markerBefore = WorkbookCompatibilityReader.AuthorityMarker(beforeRead);
        var rowsBefore = MarkerRows(beforeRead);

        // --- the premise, part 1: 00_Control carries the full governance + migration envelope, so
        // the envelope gate cannot be what refuses. If it did, this probe would be measuring a gate
        // that fired first and would say nothing about the shadowing route.
        var control = beforeRead.Sheet("Control")!;
        var missingEnvelope = DevelopmentControlEnvelopeColumns.All
            .Where(n => !control.Columns.Any(c =>
                string.Equals(c.LogicalName, n, StringComparison.Ordinal)
                && c.Found && c.PhysicalColumn is not null))
            .ToArray();
        Assert.Empty(missingEnvelope);

        // --- the premise, part 2: exactly ONE row currently declares the marker, the marker reads
        // CANDIDATE, and the value to be appended is not already present under `Value` — so the
        // identity-collision gate has nothing to refuse either.
        Assert.Single(rowsBefore);
        Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerCandidate, markerBefore);
        Assert.DoesNotContain(ShadowMarkerValue,
            control.Records.Select(r => r.Get("Value") ?? ""), StringComparer.OrdinalIgnoreCase);

        // The COLUMN is `ControlItem`; `ControlState` is the VALUE that names the marker.
        //
        // Getting those two the wrong way round was this probe's own first-run bug, and it is worth
        // recording why it matters: the run before this one was refused with "'Control' has no
        // logical column 'ControlState'" — a refusal for a mistyped KEY, which asserts
        // `Appended == false` and would have read as "the marker is safe" while measuring nothing of
        // the kind. The evidence line below carries the refusal REASON verbatim for exactly that
        // reason: the boolean alone cannot distinguish "the shadowing route is closed" from "the
        // request was malformed", and those are opposite findings.
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ControlItem"] = WorkbookCompatibilityReader.AuthorityMarkerItem,
            ["Value"] = ShadowMarkerValue,
        };

        var result = Append(fixture, Request(
            logicalSheet: "Control",
            identityColumn: "Value",
            changeId: "CHG-W8D-GAPB-0001",
            values: values,
            scope: ScopeCovering("CHG-W8D-GAPB-0001")));

        var afterRead = WorkbookCompatibilityReader.Read(fixture.Copy);
        var afterSha = fixture.Sha256();
        var markerAfter = WorkbookCompatibilityReader.AuthorityMarker(afterRead);
        var rowsAfter = MarkerRows(afterRead);

        var evidence =
            $"GapB probing Append(Control, IdentityColumn=Value, ControlItem=ControlState, Value={ShadowMarkerValue}) :: "
            + $"appended={result.Appended}; row={(result.Row?.ToString() ?? "<none>")}; "
            + $"verdict={result.Containment}; reason={result.Reason} || "
            + $"marker-declaring rows {rowsBefore.Length} -> {rowsAfter.Length} "
            + $"[before: {string.Join("; ", rowsBefore)} | after: {string.Join("; ", rowsAfter)}]; "
            + $"marker '{markerBefore}' -> '{markerAfter ?? "<UNREADABLE>"}'; "
            + $"resolved authority={new DevelopmentControlReader().Read(fixture.Copy).Authority}; "
            + $"sha {beforeSha[..12]} -> {afterSha[..12]} "
            + $"unchanged={string.Equals(beforeSha, afterSha, StringComparison.OrdinalIgnoreCase)}";
        Record(evidence);

        // --- THE SAFE OUTCOME. If the shadowing append lands, this is what fails.
        Assert.False(result.Appended, evidence);
        // ...and it must be refused ABOUT THE MARKER. `Appended == false` is satisfied by any
        // refusal at all, so on its own it cannot tell "the shadowing route is closed" from "this
        // append never works"; the control below exists for the same reason, and this assertion is
        // the cheaper half of it. The refusal must name the item the shadowing row would claim.
        Assert.Contains(WorkbookCompatibilityReader.AuthorityMarkerItem, result.Reason, StringComparison.Ordinal);
        Assert.Equal(beforeSha, afterSha);
        Assert.Single(rowsAfter);
        Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerCandidate, markerAfter);
        Assert.Equal(DevelopmentControlAuthority.Candidate,
            new DevelopmentControlReader().Read(fixture.Copy).Authority);
    }

    /// <summary>
    /// GAP B, the other direction: the same append route DEMOTES an authoritative workbook.
    ///
    /// <para>The promotion probe is the more alarming half, but the finding is "an ordinary governed
    /// write moves what the marker resolves to" in BOTH directions, and only an executed test
    /// establishes the second one. This promotes a temp copy to authoritative with the real cutover
    /// first, so the demotion is measured against a workbook that genuinely WAS the authority, and
    /// then attempts the same shadowing append.</para>
    ///
    /// <para><b>Why the demoting value is not <c>CANDIDATE</c>.</b> It cannot be. The identity
    /// collision check runs against the caller-declared <c>IdentityColumn</c>, which here is
    /// <c>Value</c>, and row 10 still carries <c>CANDIDATE</c> in that column — so re-declaring the
    /// old marker is refused as a duplicate. That is worth stating precisely, because it is the one
    /// thing that looks like a marker guard and is not one: it is an accident of which column the
    /// caller nominated as the identity. Nominate <c>Value</c> and promote with a value no row
    /// carries, and nothing refuses; the collision gate never asks whether the VALUE is a marker.</para>
    /// </summary>
    [Fact]
    public void GapB_AnOrdinaryAppend_CanAlsoDemoteTheAuthority()
    {
        using var fixture = Fixture.Candidate();

        // Promote first, through the governed cutover, so the workbook really is the authority.
        Assert.True(Cutover(fixture, CutoverRequest()).Performed);

        var beforeSha = fixture.Sha256();
        var beforeRead = WorkbookCompatibilityReader.Read(fixture.Copy);
        var markerBefore = WorkbookCompatibilityReader.AuthorityMarker(beforeRead);
        Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerAuthoritative, markerBefore);
        Assert.Equal(DevelopmentControlAuthority.Authoritative,
            new DevelopmentControlReader().Read(fixture.Copy).Authority);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ControlItem"] = WorkbookCompatibilityReader.AuthorityMarkerItem,
            ["Value"] = "W8D-DEMOTED-NOT-A-MARKER",
        };

        var result = Append(fixture, Request(
            logicalSheet: "Control",
            identityColumn: "Value",
            changeId: "CHG-W8D-GAPB-DEMOTE-0001",
            values: values,
            scope: ScopeCovering("CHG-W8D-GAPB-DEMOTE-0001")));

        var afterRead = WorkbookCompatibilityReader.Read(fixture.Copy);
        var afterSha = fixture.Sha256();
        var markerAfter = WorkbookCompatibilityReader.AuthorityMarker(afterRead);
        var rowsAfter = MarkerRows(afterRead);

        var evidence =
            $"GapB/DEMOTION probing Append(Control, IdentityColumn=Value, ControlItem=ControlState, "
            + $"Value=W8D-DEMOTED-NOT-A-MARKER) on an AUTHORITATIVE workbook :: "
            + $"appended={result.Appended}; row={(result.Row?.ToString() ?? "<none>")}; reason={result.Reason} || "
            + $"marker-declaring rows after: [{string.Join("; ", rowsAfter)}]; "
            + $"marker '{markerBefore}' -> '{markerAfter ?? "<UNREADABLE>"}'; "
            + $"resolved authority={new DevelopmentControlReader().Read(fixture.Copy).Authority}; "
            + $"sha {beforeSha[..12]} -> {afterSha[..12]} "
            + $"unchanged={string.Equals(beforeSha, afterSha, StringComparison.OrdinalIgnoreCase)}";
        Record(evidence);

        // --- THE SAFE OUTCOME: the authority cannot be demoted by an ordinary append.
        Assert.False(result.Appended, evidence);
        // Refused about the marker, not merely refused — see the note on the promotion probe.
        Assert.Contains(WorkbookCompatibilityReader.AuthorityMarkerItem, result.Reason, StringComparison.Ordinal);
        Assert.Equal(beforeSha, afterSha);
        Assert.Equal(markerBefore, markerAfter);
        Assert.Single(rowsAfter);
        Assert.Equal(DevelopmentControlAuthority.Authoritative,
            new DevelopmentControlReader().Read(fixture.Copy).Authority);
    }

    /// <summary>
    /// GAP B's mutation control, and the reason the probe above means anything.
    ///
    /// <para>The shadowing probe asserts <c>Appended == false</c>. That assertion passes for ANY
    /// refusal — a mistyped sheet name, an envelope that did not resolve, a column the form does not
    /// bind — so on its own it would report "the marker is safe" while actually measuring "append to
    /// this sheet never works". This test removes the ONE condition under test: it appends to the
    /// same sheet, under the same self-declared scope, and only changes the <c>ControlItem</c> value
    /// to one that does not name the marker. If THAT succeeds while the shadowing append is refused,
    /// the refusal is the marker guard. If this fails too, the probe above is measuring a broken
    /// append path and its verdict is void.</para>
    ///
    /// <para><b>It does not use the attack's <c>IdentityColumn</c>, and that difference is the
    /// point.</b> The attack nominated <c>Value</c> as its identity column and that nomination was
    /// load-bearing: it steered the collision check onto a column no row carried, which is how a
    /// second <c>ControlItem = "ControlState"</c> row got in. Nomination is no longer a way to choose
    /// which value must be unique. The schema declares <c>Control</c>'s identity to be
    /// <c>ControlItem</c>, and an append nominating anything else is refused outright — so the
    /// attack's own shape is closed at the root, and a control that reproduced it could only measure
    /// that closure, not reachability. This one therefore appends under the SCHEMA's identity, which
    /// is the only shape that still reaches the sheet at all, and reaching the sheet is the one thing
    /// this control exists to prove.</para>
    ///
    /// <para>It does not move the marker: it appends a new key/value pair the resolution scan has no
    /// interest in, which is what makes it a control rather than a second attack.</para>
    /// </summary>
    [Fact]
    public void GapB_AnAppendToTheControlSheetWithABenignItem_ReachesTheSheet()
    {
        using var fixture = Fixture.Candidate();

        var beforeSha = fixture.Sha256();
        var markerBefore = WorkbookCompatibilityReader.AuthorityMarker(
            WorkbookCompatibilityReader.Read(fixture.Copy));

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // A name no row carries, so the append is a genuinely NEW record and not a collision
            // with one of `00_Control`'s existing items.
            ["ControlItem"] = "W8D-GapB-Benign-Item",
            ["Value"] = "w8d-probe-value",
        };

        var result = Append(fixture, Request(
            logicalSheet: "Control",
            // The schema's identity, not the attack's. See the note on this test.
            identityColumn: "ControlItem",
            changeId: "CHG-W8D-GAPB-CONTROL-0001",
            values: values,
            scope: ScopeCovering("CHG-W8D-GAPB-CONTROL-0001")));

        var afterRead = WorkbookCompatibilityReader.Read(fixture.Copy);
        var afterSha = fixture.Sha256();
        var markerAfter = WorkbookCompatibilityReader.AuthorityMarker(afterRead);

        var evidence =
            $"GapB control — benign Append(Control, IdentityColumn=ControlItem, "
            + "ControlItem=W8D-GapB-Benign-Item) :: "
            + $"appended={result.Appended}; row={(result.Row?.ToString() ?? "<none>")}; reason={result.Reason} || "
            + $"marker '{markerBefore}' -> '{markerAfter ?? "<UNREADABLE>"}'; "
            + $"sha {beforeSha[..12]} -> {afterSha[..12]} "
            + $"changed={!string.Equals(beforeSha, afterSha, StringComparison.OrdinalIgnoreCase)}";
        Record(evidence);

        // The control must APPEND: that is what proves the shadowing probe's refusal, if any, is
        // about the marker. It must also leave the marker alone, which is the property under test.
        Assert.True(result.Appended, evidence);
        Assert.NotEqual(beforeSha, afterSha);
        Assert.Equal(markerBefore, markerAfter);
        Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerCandidate, markerAfter);
        Assert.Equal(DevelopmentControlAuthority.Candidate,
            new DevelopmentControlReader().Read(fixture.Copy).Authority);
    }

    // ================================================================ W8D PRODUCTION WIRING — regression
    //
    // The probes above close the two routes they were written for. These do something a route test
    // cannot: they state the PROPERTY, so that closing an observed route is not mistaken for closing
    // the class. That distinction earned its keep here — the sweep for "what else can shadow the
    // marker" turned up a third governed route (see leg (b) below) that neither probe touched and
    // that the row-scoped identity guard alone would have left open.

    /// <summary>
    /// THE PROPERTY: no ordinary governed write and no ordinary governed append can change what the
    /// authority marker resolves to.
    ///
    /// <para>Stated over the operation rather than over the CVE. Four legs, each on its own fixture,
    /// because a refusal is only evidence when the thing that would otherwise have happened is
    /// visible:</para>
    /// <list type="bullet">
    /// <item><description><b>(a)</b> the marker row's own identity cell — the probe's route, kept
    /// here so this test fails if that guard is ever removed.</description></item>
    /// <item><description><b>(b)</b> a DIFFERENT row's identity cell claiming the marker's item
    /// name. This is the route the sweep found: <c>00_Control</c> rows 11–22 are ordinary, writable,
    /// non-envelope rows, and the resolver locates the marker by the item NAME, so renaming row 11's
    /// <c>ControlItem</c> to <c>ControlState</c> makes a second marker row — after which last-wins in
    /// ascending row order hands the authority to row 11. Guarding only the marker's own row leaves
    /// this entirely open.</description></item>
    /// <item><description><b>(c)</b> an append declaring the marker's item name.</description></item>
    /// <item><description><b>(d)</b> the CONTROL: the same column, the same operation, with a value
    /// that does not claim the marker's name, must still SUCCEED. Without it, every assertion above
    /// also passes if the column was simply frozen — which would report "the marker is safe" while
    /// measuring "governed writes to this sheet stopped working".</description></item>
    /// </list>
    ///
    /// <para>Each leg asserts the bytes are unchanged, not merely that a refusal was reported: a
    /// guard that returned "refused" while writing anyway passes every other assertion here.</para>
    /// </summary>
    [Fact]
    public void NoOrdinaryGovernedWriteOrAppend_CanMoveWhatTheAuthorityMarkerResolvesTo()
    {
        DevelopmentControlWriterAuthorizer Writer() => new();

        // ---- (a) the marker row's identity cell: 00_Control!A10.
        using (var fixture = Fixture.Candidate())
        {
            var beforeSha = fixture.Sha256();
            using var reservation = Hold(fixture, "w8d-reg-a");
            var write = Writer().Write(reservation,
                new DevelopmentControlCellWrite("Control", 10, "ControlItem", "ControlStateRenamed"));

            Assert.False(write.Written, write.Reason);
            Assert.Equal(DevelopmentControlWriteVerdict.RefusedAuthoritySite, write.Verdict);
            Assert.Contains("GOVERNED_CELL", write.Reason, StringComparison.Ordinal);
            Assert.Equal(beforeSha, fixture.Sha256());
            Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerCandidate,
                WorkbookCompatibilityReader.AuthorityMarker(WorkbookCompatibilityReader.Read(fixture.Copy)));

            Record($"REG (a) marker row identity write refused :: verdict={write.Verdict}; "
                 + $"sha unchanged=True; marker still CANDIDATE");
        }

        // ---- (b) ANOTHER row's identity cell, claiming the marker's item name.
        using (var fixture = Fixture.Candidate())
        {
            var beforeRead = WorkbookCompatibilityReader.Read(fixture.Copy);
            var beforeSha = fixture.Sha256();

            // The premise, asserted rather than assumed: row 11 is a real row with a real item, it is
            // not the marker row, and it is not an authority site at either layer — so nothing except
            // the reserved-name rule stands between it and the marker.
            Assert.Equal("MigrationVersion", beforeRead.Sheet("Control")!.Records.Single(r => r.Row == 11).Get("ControlItem"));
            Assert.Null(DevelopmentControlAuthoritySites.FindPhysical("00_Control", "A", 11));
            Assert.Null(DevelopmentControlAuthoritySites.FindLogical("Control", 11, "ControlItem"));
            Assert.Null(DevelopmentControlAuthoritySites.FindPhysicalIdentity("00_Control", "A", 11));
            Assert.Single(MarkerRows(beforeRead));

            using var reservation = Hold(fixture, "w8d-reg-b");
            var write = Writer().Write(reservation,
                new DevelopmentControlCellWrite("Control", 11, "ControlItem",
                    WorkbookCompatibilityReader.AuthorityMarkerItem));

            var afterRead = WorkbookCompatibilityReader.Read(fixture.Copy);
            var evidence =
                $"REG (b) second-row identity claim Write(Control, 11, ControlItem, ControlState) :: "
                + $"written={write.Written}; verdict={write.Verdict}; reason={write.Reason} || "
                + $"marker-declaring rows {MarkerRows(beforeRead).Length} -> {MarkerRows(afterRead).Length}; "
                + $"marker='{WorkbookCompatibilityReader.AuthorityMarker(afterRead) ?? "<UNREADABLE>"}'; "
                + $"sha unchanged={string.Equals(beforeSha, fixture.Sha256(), StringComparison.OrdinalIgnoreCase)}";
            Record(evidence);

            // If this route were open, `ControlItems` would carry row 11's value under `ControlState`
            // and the resolver would report Unknown — or, on a workbook where some later row carried
            // AUTHORITATIVE, would promote it. Either way the marker moved.
            Assert.False(write.Written, evidence);
            Assert.Equal(DevelopmentControlWriteVerdict.RefusedAuthoritySite, write.Verdict);
            Assert.Contains(WorkbookCompatibilityReader.AuthorityMarkerItem, write.Reason, StringComparison.Ordinal);
            Assert.Equal(beforeSha, fixture.Sha256());
            Assert.Single(MarkerRows(afterRead));
            Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerCandidate,
                WorkbookCompatibilityReader.AuthorityMarker(afterRead));
        }

        // ---- (c) an append declaring the marker's item name.
        using (var fixture = Fixture.Candidate())
        {
            var beforeSha = fixture.Sha256();
            var beforeRead = WorkbookCompatibilityReader.Read(fixture.Copy);

            // The change id is deliberately NOT overridden here: the declared scope authorises
            // `CHG-W8D-APPEND-0001`, and a record stamped with anything else is refused for that
            // reason first — which would make this leg pass without ever reaching the marker guard
            // it exists to exercise. The refusal REASON is asserted below for the same purpose.
            var result = Append(fixture, Request(
                logicalSheet: "Control",
                identityColumn: "Value",
                values: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ControlItem"] = WorkbookCompatibilityReader.AuthorityMarkerItem,
                    ["Value"] = WorkbookCompatibilityReader.AuthorityMarkerAuthoritative,
                }));

            var afterRead = WorkbookCompatibilityReader.Read(fixture.Copy);
            var evidence =
                $"REG (c) append claiming the marker name :: appended={result.Appended}; "
                + $"reason={result.Reason} || marker-declaring rows {MarkerRows(beforeRead).Length} -> "
                + $"{MarkerRows(afterRead).Length}; "
                + $"marker='{WorkbookCompatibilityReader.AuthorityMarker(afterRead) ?? "<UNREADABLE>"}'; "
                + $"sha unchanged={string.Equals(beforeSha, fixture.Sha256(), StringComparison.OrdinalIgnoreCase)}";
            Record(evidence);

            Assert.False(result.Appended, evidence);
            Assert.Contains(WorkbookCompatibilityReader.AuthorityMarkerItem, result.Reason, StringComparison.Ordinal);
            Assert.Equal(beforeSha, fixture.Sha256());
            Assert.Single(MarkerRows(afterRead));
            Assert.Equal(DevelopmentControlAuthority.Candidate,
                new DevelopmentControlReader().Read(fixture.Copy).Authority);
        }

        // ---- (d) CONTROL: the same column and the same operation, without the marker's name.
        using (var fixture = Fixture.Candidate())
        {
            var beforeSha = fixture.Sha256();
            var markerBefore = WorkbookCompatibilityReader.AuthorityMarker(
                WorkbookCompatibilityReader.Read(fixture.Copy));

            using var reservation = Hold(fixture, "w8d-reg-d");
            var write = Writer().Write(reservation,
                new DevelopmentControlCellWrite("Control", 22, "ControlItem", "W8D-Probe-Item-Renamed"));

            var afterRead = WorkbookCompatibilityReader.Read(fixture.Copy);
            var evidence =
                $"REG (d) control — benign identity write to the same column :: written={write.Written}; "
                + $"verdict={write.Verdict}; reason={write.Reason} || marker '{markerBefore}' -> "
                + $"'{WorkbookCompatibilityReader.AuthorityMarker(afterRead) ?? "<UNREADABLE>"}'; "
                + $"sha changed={!string.Equals(beforeSha, fixture.Sha256(), StringComparison.OrdinalIgnoreCase)}";
            Record(evidence);

            Assert.True(write.Written, evidence);
            Assert.Equal(DevelopmentControlWriteVerdict.Allowed, write.Verdict);
            Assert.NotEqual(beforeSha, fixture.Sha256());

            // And it did NOT move the marker — a rename that does not claim the marker's name is an
            // ordinary governed edit, which is the whole reason the rule above is about the VALUE.
            Assert.Equal(markerBefore, WorkbookCompatibilityReader.AuthorityMarker(afterRead));
            Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerCandidate,
                WorkbookCompatibilityReader.AuthorityMarker(afterRead));
            Assert.Single(MarkerRows(afterRead));
        }
    }

    /// <summary>
    /// THE CLASS, not the routes: a workbook in which the marker's item name is declared TWICE must
    /// resolve its authority as unreadable, never to whichever row came last.
    ///
    /// <para><b>Why this cannot be done through the component.</b> After the guards above, no
    /// governed operation can create the ambiguity — which is the point of them, and also why a test
    /// that used one would be measuring the writer over again rather than the reader's rule. The
    /// writer is not the only thing that can put a row in a sheet: an external editor, a repair
    /// script or a second tool can each do it without ever calling this component, and a resolution
    /// rule that GUESSES between two declarations is wrong however the second one arrived. So the
    /// ambiguity is planted at the byte level here, deliberately outside this component, and the
    /// reader is asked what it makes of it.</para>
    ///
    /// <para><b>The planted value is <c>AUTHORITATIVE</c>, and the row is the LAST one.</b> That is
    /// the worst case on purpose: under last-wins it would PROMOTE the workbook, which is the exact
    /// outcome the directive forbids and the one a silent rule would produce. The injection is
    /// asserted to have landed before the verdict is read, because an injection that failed would
    /// otherwise make this test pass by measuring nothing at all — the same trap the probes above
    /// carry their premises for.</para>
    /// </summary>
    [Fact]
    public void ADuplicatedMarkerRow_PlantedOutsideTheComponent_ResolvesToNothingRatherThanToTheShadow()
    {
        using var fixture = Fixture.Candidate();

        // Row 22 is the candidate's last row, so the planted row resolves LAST under the old
        // last-wins rule — the shadowing position, not the shadowed one.
        ForceCellDirectly(fixture.Copy, "00_Control", "A22", WorkbookCompatibilityReader.AuthorityMarkerItem);
        ForceCellDirectly(fixture.Copy, "00_Control", "B22", WorkbookCompatibilityReader.AuthorityMarkerAuthoritative);

        var read = WorkbookCompatibilityReader.Read(fixture.Copy);

        // --- the premise: the ambiguity is really there. Two rows now declare the marker item, and
        // the planted one carries AUTHORITATIVE.
        var rows = MarkerRows(read);
        Assert.Equal(2, rows.Length);
        Assert.Contains($"row 22 => '{WorkbookCompatibilityReader.AuthorityMarkerAuthoritative}'", rows);

        var items = WorkbookCompatibilityReader.ControlItems(read);

        // --- the rule, stated per key: the ambiguous key resolves to nothing, and its unambiguous
        // neighbours are untouched. Asserting both halves is what distinguishes "fail closed on the
        // duplicated key" from "the projection stopped working".
        Assert.False(items.ContainsKey(WorkbookCompatibilityReader.AuthorityMarkerItem));
        Assert.Equal("NEXUS-DEVELOPMENTCONTROL", items["SchemaId"]);
        Assert.Equal("V3.0", items["SchemaVersion"]);

        // --- the consequence that matters: the marker does not resolve to the shadow, and the
        // workbook becomes loudly unwritable rather than quietly authoritative.
        var marker = WorkbookCompatibilityReader.AuthorityMarker(read);
        var authorisation = WorkbookCompatibilityReader.AuthorizeWrite(read, WorkbookForm.V3);

        var evidence =
            $"REG duplicate-marker-resolution :: marker-declaring rows=[{string.Join("; ", rows)}]; "
            + $"ControlItems['{WorkbookCompatibilityReader.AuthorityMarkerItem}'] present={items.ContainsKey(WorkbookCompatibilityReader.AuthorityMarkerItem)}; "
            + $"AuthorityMarker='{marker ?? "<null>"}' (NOT '{WorkbookCompatibilityReader.AuthorityMarkerAuthoritative}'); "
            + $"AuthorizeWrite={authorisation.Verdict}; allowed={authorisation.Allowed}";
        Record(evidence);

        Assert.Null(marker);
        Assert.False(authorisation.Allowed, evidence);
        Assert.Equal(WorkbookCompatibilityReader.WriteVerdict.RefusedAuthorityMarkerUnreadable,
            authorisation.Verdict);
    }

    /// <summary>
    /// Resolves a physical sheet name to its part inside the package the way the component does —
    /// through <c>xl/workbook.xml</c> and its relationship map, never by assuming a part name.
    /// Shared by the byte-level helper below so that the injection addresses the same part the
    /// reader does.
    /// </summary>
    private static string SheetPartOf(string workbookPath, string physicalSheetName)
    {
        System.Xml.Linq.XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        System.Xml.Linq.XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        System.Xml.Linq.XNamespace p = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var zip = System.IO.Compression.ZipFile.OpenRead(workbookPath);

        System.Xml.Linq.XDocument Load(string part)
        {
            using var stream = zip.GetEntry(part)!.Open();
            return System.Xml.Linq.XDocument.Load(stream);
        }

        var targets = Load("xl/_rels/workbook.xml.rels").Root!
            .Elements(p + "Relationship")
            .ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!,
                StringComparer.Ordinal);

        var sheet = Load("xl/workbook.xml").Root!.Element(m + "sheets")!.Elements(m + "sheet")
            .First(s => string.Equals((string?)s.Attribute("name"), physicalSheetName, StringComparison.Ordinal));

        return targets[(string)sheet.Attribute(r + "id")!].TrimStart('/');
    }

    /// <summary>
    /// Sets ONE cell of a physical sheet to an inline string, by editing the package DIRECTLY —
    /// deliberately not through this component.
    ///
    /// <para>This exists to model the adversary the resolution rule is written for: something that
    /// can put a row or a value in a sheet without holding the lock, without a reservation and
    /// without passing a single guard. It is test-only and it never targets the live authority —
    /// every caller hands it a <see cref="Fixture"/> copy in a temp directory.</para>
    /// </summary>
    private static void ForceCellDirectly(
        string workbookPath, string physicalSheetName, string cellReference, string value)
    {
        System.Xml.Linq.XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var part = SheetPartOf(workbookPath, physicalSheetName);

        using var zip = System.IO.Compression.ZipFile.Open(
            workbookPath, System.IO.Compression.ZipArchiveMode.Update);

        var entry = zip.GetEntry(part)!;

        System.Xml.Linq.XDocument doc;
        using (var stream = entry.Open()) doc = System.Xml.Linq.XDocument.Load(stream);

        var rowNumber = int.Parse(new string(cellReference.SkipWhile(char.IsLetter).ToArray()),
            System.Globalization.CultureInfo.InvariantCulture);

        var row = doc.Root!.Element(m + "sheetData")!.Elements(m + "row")
            .First(e => (string?)e.Attribute("r") == rowNumber.ToString(
                System.Globalization.CultureInfo.InvariantCulture));

        var replacement = new System.Xml.Linq.XElement(m + "c",
            new System.Xml.Linq.XAttribute("r", cellReference),
            new System.Xml.Linq.XAttribute("t", "inlineStr"),
            new System.Xml.Linq.XElement(m + "is", new System.Xml.Linq.XElement(m + "t", value)));

        row.Elements(m + "c")
            .First(c => string.Equals((string?)c.Attribute("r"), cellReference, StringComparison.OrdinalIgnoreCase))
            .ReplaceWith(replacement);

        using var buffer = new MemoryStream();
        doc.Save(buffer, System.Xml.Linq.SaveOptions.DisableFormatting);

        using var target = entry.Open();
        target.SetLength(0);
        buffer.Position = 0;
        buffer.CopyTo(target);
    }
}
