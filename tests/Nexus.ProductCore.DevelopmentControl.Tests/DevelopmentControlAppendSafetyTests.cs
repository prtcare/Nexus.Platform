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
public sealed class DevelopmentControlAppendSafetyTests
{
    private static string V3Workbook =>
        Environment.GetEnvironmentVariable("W1_V3_WORKBOOK")
        ?? @"D:\NEXUS\DevelopmentControl\NEXUS_DEVELOPMENT_CONTROL.xlsx";

    /// <summary>The preserved 14-sheet revision. Read-only in every sense: tests copy it, and the
    /// component must refuse to append to the copy.</summary>
    private static string LegacyWorkbook =>
        Environment.GetEnvironmentVariable("W1_LEGACY_WORKBOOK")
        ?? @"D:\NEXUS\Archives\Legacy-DevelopmentControl"
         + @"\NEXUS_DEVELOPMENT_CONTROL_20260830_pre-cutover-backup.xlsx";

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
            LockDir = Path.Combine(Root, "locks");
            Directory.CreateDirectory(LockDir);
            Copy = Path.Combine(Root, "NEXUS_DEVELOPMENT_CONTROL.xlsx");
            File.Copy(source, Copy, overwrite: false);
        }

        public static Fixture Candidate() => new(V3Workbook);
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

    /// <summary>A declaration that DOES cover the store for write — the control baseline.</summary>
    private static ChangeScopeDeclaration ScopeCovering() =>
        new("w8d-append-lane", "CHG-W8D-APPEND-0001",
            [new ChangeScopeItem(ChangeScopeItemKind.ControlStore,
                ChangeScopeAccessMode.Write, "NEXUS_DEVELOPMENT_CONTROL.xlsx")]);

    private static IDevelopmentControlReservation Hold(Fixture fixture, string owner = "w8d-t3") =>
        new DevelopmentControlLockService().TryAcquire(fixture.Copy, fixture.LockDir, owner).Reservation
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

        using var candidate = Fixture.Candidate();
        Assert.True(Append(candidate, Request(scope: ScopeCovering())).Appended);
    }

    [Fact]
    public void Append_Refuses_WithoutAReservation()
    {
        using var fixture = Fixture.Candidate();
        var before = fixture.Sha256();

        var service = new DevelopmentControlLockService();
        var attempt = service.TryAcquire(fixture.Copy, fixture.LockDir, "w8d-t3-released");
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
            .TryAcquire(fixture.Copy, fixture.LockDir, "contender");

        Assert.NotEqual(DevelopmentControlLockOutcome.Acquired, contender.Outcome);
        Assert.Null(contender.Reservation);
        Record($"T3.3 contention :: outcome={contender.Outcome} reservation={(contender.Reservation is null ? "none" : "ISSUED")}");

        // Control: once the holder releases, the same contender DOES acquire — so the refusal above
        // is contention and not a broken lock service.
        holder.Dispose();
        var second = new DevelopmentControlLockService()
            .TryAcquire(fixture.Copy, fixture.LockDir, "contender");
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

        Assert.Equal(new[] { "xl/worksheets/sheet14.xml" }, changed);   // 13_GitLineage

        // The shared string table is byte-identical: the writer appended no string to it.
        Assert.Equal(beforeParts["xl/sharedStrings.xml"], afterParts["xl/sharedStrings.xml"]);

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
    /// <para><b>What is actually being proved, and what the control is for.</b> Append cannot reach
    /// the marker for a structural reason: it only ever brings a NEW row into existence, and the
    /// marker is an existing cell on an existing row. That argument is sound but unfalsifiable on
    /// its own — a test asserting "the marker did not change" passes trivially if the marker
    /// assertion is broken, if the wrong cell is being read, or if the fixture is stale.</para>
    ///
    /// <para>So this test FIRST proves the assertion is sensitive: it uses the cell-write path to
    /// change the very cell, on a separate disposable copy, and shows the reading does move. Only
    /// then does it assert that append leaves it alone. The two halves together are the mutation
    /// control — without the first, the second measures nothing.</para>
    /// </summary>
    [Fact]
    public void Append_CannotMutateTheAuthorityMarker_AndTheCheckThatSaysSoIsSensitive()
    {
        // ---- Half 1: the sensor works. Point the CELL writer at the marker cell and watch it move.
        using (var probe = Fixture.Candidate())
        {
            var beforeMarker = WorkbookCompatibilityReader.AuthorityMarker(
                WorkbookCompatibilityReader.Read(probe.Copy));
            Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerCandidate, beforeMarker);

            using var reservation = Hold(probe, "w8d-t3-marker-probe");
            var write = new DevelopmentControlWriterAuthorizer().Write(
                reservation,
                new DevelopmentControlCellWrite("Control", 10, "Value",
                    WorkbookCompatibilityReader.AuthorityMarkerAuthoritative));

            Assert.True(write.Written, write.Reason);

            var afterMarker = WorkbookCompatibilityReader.AuthorityMarker(
                WorkbookCompatibilityReader.Read(probe.Copy));

            Assert.Equal(WorkbookCompatibilityReader.AuthorityMarkerAuthoritative, afterMarker);
            Assert.NotEqual(beforeMarker, afterMarker);

            Record($"T3.5 marker sensor sensitive :: {beforeMarker} -> {afterMarker} via the cell path");
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
}
