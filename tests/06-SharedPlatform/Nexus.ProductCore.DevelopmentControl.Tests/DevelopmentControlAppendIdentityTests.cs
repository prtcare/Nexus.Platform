using Nexus.DevelopmentControl.Safety;
using Nexus.ProductCore.Contracts.DevelopmentControl;
using Nexus.ProductCore.Core.DevelopmentControl;
using Xunit;

namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// W8D COMPLETION — the append identity rule, asserted on EVERY supported append target.
///
/// <para><b>The defects this suite is written against.</b> All three were reproduced through the
/// public <c>DevelopmentControlWriterAuthorizer.Append</c> against the live authority and the
/// archived candidate, and all three have the same root: identity was whatever the CALLER declared
/// in <c>DevelopmentControlAppendRecord.IdentityColumn</c>, and nothing checked that nomination
/// against the schema.</para>
///
/// <list type="number">
/// <item>A duplicate immutable id was ACCEPTED by nominating some other column — the collision check
/// then compared that column, found it distinct, and let a second row carry an id that already
/// existed.</item>
/// <item>A BLANK immutable id was accepted the same way.</item>
/// <item>A record stamped with one change id while its declared scope authorised another was
/// accepted, so its envelope named an origin that never authorised it.</item>
/// </list>
///
/// <para><b>Why every target is enumerated rather than one exemplar.</b> The obvious implementation
/// of this fix is a hand-maintained switch mapping sheet to key column, and such a table drifts: the
/// patch that prompted this work mapped <c>Dependencies</c> to a column that does not exist and
/// <c>ExistingAssets</c> to a key it cannot have, while omitting <c>Control</c>, whose appends work.
/// So the identity rule lives in the schema (<see cref="SheetBinding.V3ImmutableIdentity"/>) and the
/// tests below are driven from a table that must agree with the schema column for column. Adding a
/// target, or renaming a key column, without teaching this suite about it fails
/// <see cref="The_schema_declares_identity_for_exactly_the_targets_this_suite_covers"/>.</para>
///
/// <para><b>Every refusal carries a positive control.</b> <c>Appended == false</c> is satisfied by
/// any refusal at all, so a negative test alone cannot distinguish "the guard held" from "the request
/// never arrived". Each refusal below therefore also asserts WHY, in the refusal's own words, and
/// each has a twin that differs by exactly one condition and must succeed.</para>
///
/// <para><b>Nothing here writes to the real authority.</b> Every case copies the preserved
/// pre-cutover candidate to a throwaway directory and appends there. The authoritative workbook is
/// never opened for write by this file, and no case demonstrates a refusal by damaging one.</para>
/// </summary>
// W10.0A FINAL — LANE: WindowsOnly, at CLASS level. Same reason as
// DevelopmentControlAppendSafetyTests: every test here appends, and the append path acquires a
// reservation whose lock file name comes from a normaliser that requires a drive-rooted path. See
// that file's class comment for the measurement, and PORTABLE_TEST_ARCHITECTURE.md for the contract.
[Trait("Lane", "WindowsOnly")]
public sealed class DevelopmentControlAppendIdentityTests
{
    /// <summary>
    /// A reconciled V3 CANDIDATE — the state the rest of the W8D suites fixture against, so an append
    /// that lands correctly must not promote it.
    ///
    /// <para>
    /// W10.0A FINAL: generated rather than read from the estate's preserved pre-cutover revision.
    /// Everything asserted in this file is about identity binding and the append contract, and none of
    /// it depends on which file the workbook came from — which is exactly why it must not have needed
    /// one developer's machine to run. See <see cref="WorkbookFixtures"/>.
    /// </para>
    /// </summary>
    private static string CandidateWorkbook => WorkbookFixtures.Candidate;

    /// <summary>The change every well-formed request in this file is written under.</summary>
    private const string Change = "CHG-W8D-IDN-0001";

    // ================================================================ the covered targets

    /// <summary>
    /// The covered append targets, shared by every theory in this file so that the drift tests
    /// compare ONE table against the schema rather than a copy of it.
    ///
    /// <para>Row shape: the logical sheet, the column a caller nominates as the key (one member of a
    /// composite), and a real NON-key business column of that sheet. The third is load-bearing — the
    /// negative cases nominate it, so it must exist for the nomination to reach the identity guard
    /// rather than fail as an unknown column.
    /// <see cref="Every_covered_non_key_column_is_a_real_business_column_of_its_sheet"/> holds that
    /// property to the schema, because a mistyped column there would make every negative case pass
    /// for the wrong reason.</para>
    /// </summary>
    public static IEnumerable<object[]> AppendTargets() =>
    [
        ["Control", "ControlItem", "Value"],
        ["WorkGraph", "WorkId", "Title"],
        ["Changes", "ChangeId", "Title"],
        ["Decisions", "DecisionId", "Title"],
        ["Architecture", "EntityId", "Name"],
        ["Dependencies", "SourceId", "Status"],
        ["GitLineage", "LineageId", "Branch"],
        ["ChangeScopes", "ScopeRecordId", "ScopeClass"],
        ["ChangeRequests", "RequestId", "RequestType"],
        ["MigrationMap", "MapId", "ComponentId"],
        ["NexusEvolution", "EvolutionId", "Sequence"],
    ];

    [Theory]
    [MemberData(nameof(AppendTargets))]
    public void Every_supported_append_target_accepts_a_well_formed_append(
        string sheet, string identity, string nonKey)
    {
        using var fixture = new Fixture();

        var result = Append(fixture, Request(sheet, identity, Values(sheet, nonKey)));

        // The positive control for the whole file: if this does not hold, every refusal below is
        // measuring a broken append path rather than a guard.
        Assert.True(result.Appended, $"{sheet}: {result.Reason}");
        Assert.NotNull(result.Row);

        // A write is not a cutover: appending to the candidate must leave it a candidate.
        Assert.Equal(DevelopmentControlAuthority.Candidate,
            new DevelopmentControlReader().Read(fixture.Copy).Authority);

        Record($"[IDN] {sheet} append accepted at row {result.Row}; key={result.RecordKey}");
    }

    /// <summary>
    /// The drift detector, and the reason the theories above can be trusted to cover everything.
    ///
    /// <para>It compares this file's own table with the schema's declared append targets. A new append
    /// target therefore fails this test until it is covered here, and a target removed from the schema
    /// fails it until its row is deleted — in both directions, which is what "drift" means.</para>
    /// </summary>
    [Fact]
    public void The_schema_declares_identity_for_exactly_the_targets_this_suite_covers()
    {
        var covered = CoveredTargets();

        var declared = WorkbookCompatibilityMap.Sheets
            .Where(b => b.V3ImmutableIdentity is { Count: > 0 })
            .Select(b => b.LogicalName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(declared, covered.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The declared identity of each covered sheet, checked column for column against the table the
    /// theories drive from. The test above proves every target is COVERED; this one proves each is
    /// covered with the right KEY, which a name-only comparison would not notice.
    /// </summary>
    [Fact]
    public void Each_covered_target_declares_the_identity_this_suite_asserts_against()
    {
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Control"] = ["ControlItem"],
            ["WorkGraph"] = ["WorkId"],
            ["Changes"] = ["ChangeId"],
            ["Decisions"] = ["DecisionId"],
            ["Architecture"] = ["EntityId"],
            // The one COMPOSITE key in the schema. Declared as a tuple because no single member is
            // unique — SourceId alone would refuse a legitimate second edge between the same pair.
            ["Dependencies"] = ["SourceId", "TargetId", "RelationType"],
            ["GitLineage"] = ["LineageId"],
            ["ChangeScopes"] = ["ScopeRecordId"],
            ["ChangeRequests"] = ["RequestId"],
            ["MigrationMap"] = ["MapId"],
            ["NexusEvolution"] = ["EvolutionId"],
        };

        foreach (var (sheet, key) in expected)
        {
            var binding = WorkbookCompatibilityMap.Sheets
                .SingleOrDefault(b => string.Equals(b.LogicalName, sheet, StringComparison.Ordinal));

            Assert.NotNull(binding);
            Assert.Equal(key, binding!.V3ImmutableIdentity);
        }

        // ...and a sheet that is NOT a target declares no identity, whatever else it carries.
        // `ExistingAssets` resolves to 14 envelope columns and no business column at all, so there is
        // nothing on it that could serve as one.
        var notATarget = WorkbookCompatibilityMap.Sheets
            .SingleOrDefault(b => string.Equals(b.LogicalName, "ExistingAssets", StringComparison.Ordinal));

        Assert.NotNull(notATarget);
        Assert.True(notATarget!.V3ImmutableIdentity is null or { Count: 0 },
            "'ExistingAssets' must not declare an append identity");
    }

    /// <summary>
    /// Every non-key column named in the table is a REAL business column of that sheet, and is not
    /// part of its identity.
    ///
    /// <para>Without this the negative cases could pass vacuously: nominating a column the sheet does
    /// not have is refused too, so an assertion of <c>Appended == false</c> would hold while the
    /// identity guard was never consulted. The assertion that the refusal NAMES the identity guard
    /// covers the same ground from the other side, and the two together are what make the negatives
    /// mean something.</para>
    /// </summary>
    [Fact]
    public void Every_covered_non_key_column_is_a_real_business_column_of_its_sheet()
    {
        using var fixture = new Fixture();
        var read = WorkbookCompatibilityReader.Read(fixture.Copy);

        foreach (var row in AppendTargets())
        {
            var sheet = (string)row[0]!;
            var nonKey = (string)row[2]!;

            var bound = read.Sheets.SingleOrDefault(s =>
                string.Equals(s.LogicalName, sheet, StringComparison.Ordinal));
            Assert.NotNull(bound);

            var column = bound!.Columns.SingleOrDefault(c =>
                string.Equals(c.LogicalName, nonKey, StringComparison.Ordinal));

            // `Found` means it resolved against this sheet's V3 header row — the same resolution the
            // append path uses, so a column that fails here would fail there too, as an
            // unknown-column refusal rather than the identity refusal under test.
            Assert.True(column is not null, $"'{sheet}' has no column '{nonKey}'");
            Assert.True(column!.Found, $"'{sheet}.{nonKey}' did not resolve against the V3 model");
            Assert.False(
                DevelopmentControlEnvelopeColumns.All.Contains(nonKey, StringComparer.OrdinalIgnoreCase),
                $"'{sheet}.{nonKey}' is an envelope column, not a business one");
            Assert.DoesNotContain(nonKey,
                WorkbookCompatibilityMap.Sheets
                    .Single(b => string.Equals(b.LogicalName, sheet, StringComparison.Ordinal))
                    .V3ImmutableIdentity!,
                StringComparer.Ordinal);
        }
    }

    // ================================================================ defects 1 and 2 — caller-declared identity

    /// <summary>
    /// Defect 1/2, on every target: nominating a real but non-key column is REFUSED.
    ///
    /// <para>Before the fix this was accepted, and accepting it was not cosmetic — it redirected the
    /// duplicate and blank checks onto the nominated column, which is what let a second row carry an
    /// id that already existed. The assertion names the guard, because on this input a bare
    /// <c>Appended == false</c> would also be satisfied by an unknown-column refusal.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AppendTargets))]
    public void Every_supported_append_target_refuses_a_non_identity_column_nominated_as_the_key(
        string sheet, string identity, string nonKey)
    {
        using var fixture = new Fixture();
        var before = fixture.Sha256();

        var result = Append(fixture, Request(sheet, nonKey, Values(sheet, nonKey)));

        Assert.False(result.Appended, $"{sheet}: nominating '{nonKey}' was accepted");
        Assert.Contains("is not the immutable identity", result.Reason, StringComparison.Ordinal);
        Assert.Equal(before, fixture.Sha256());

        // The twin: the same request, nominating the schema's own column, succeeds.
        using var control = new Fixture();
        Assert.True(Append(control, Request(sheet, identity, Values(sheet, nonKey))).Appended,
            $"{sheet}: the positive twin was refused");

        Record($"[IDN] {sheet} refused non-key nomination '{nonKey}': {result.Reason}");
    }

    /// <summary>
    /// Defect 1 in its damaging form, on every target: a DUPLICATE immutable id is refused even when
    /// the caller nominates a different column to hide it.
    ///
    /// <para>This is the case that matters. The first append establishes a record; the second carries
    /// the SAME identity value but nominates <paramref name="nonKey"/> as the key and gives that
    /// column a distinct value. Before the fix the collision check compared the nominated column,
    /// saw two different values, and admitted a second row with an id that already existed.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AppendTargets))]
    public void Every_supported_append_target_refuses_a_duplicate_id_hidden_behind_another_column(
        string sheet, string identity, string nonKey)
    {
        using var fixture = new Fixture();

        Assert.True(Append(fixture, Request(sheet, identity, Values(sheet, nonKey))).Appended,
            $"{sheet}: the establishing append");

        // Same identity values, different nominated column, distinct value in that column.
        var second = Values(sheet, nonKey);
        second[nonKey] = "W8DIDN-SECOND-" + sheet.ToUpperInvariant();

        var result = Append(fixture, Request(sheet, nonKey, second));

        Assert.False(result.Appended, $"{sheet}: a duplicate identity was admitted");
        // Named precisely: the nomination itself is refused now, so the duplicate cannot reach the
        // collision check at all. That IS the root fix — the check runs on the schema's column.
        Assert.Contains("is not the immutable identity", result.Reason, StringComparison.Ordinal);

        // ...and the duplicate is refused the direct way too, so the protection does not rest solely
        // on the nomination guard.
        var direct = Append(fixture, Request(sheet, identity, Values(sheet, nonKey)));
        Assert.False(direct.Appended, $"{sheet}: a duplicate identity was admitted by direct nomination");
        Assert.Contains("already exists", direct.Reason, StringComparison.Ordinal);

        Record($"[IDN] {sheet} duplicate refused both ways: {direct.Reason}");
    }

    /// <summary>
    /// Defect 2, on every target: a BLANK immutable id is refused even when the caller nominates a
    /// different, non-blank column.
    /// </summary>
    [Theory]
    [MemberData(nameof(AppendTargets))]
    public void Every_supported_append_target_refuses_a_blank_id_hidden_behind_another_column(
        string sheet, string identity, string nonKey)
    {
        using var fixture = new Fixture();

        var values = Values(sheet, nonKey);
        values[identity] = "";

        // Nominated `nonKey`, which is non-blank — so before the fix the blank check looked at a
        // column that was fine and the record was written with no identity at all.
        var result = Append(fixture, Request(sheet, nonKey, values));

        Assert.False(result.Appended, $"{sheet}: a blank identity was admitted");
        Assert.Contains("is not the immutable identity", result.Reason, StringComparison.Ordinal);

        // And refused the direct way, where the blankness itself is what stops it.
        using var control = new Fixture();
        var direct = Append(control, Request(sheet, identity, values));
        Assert.False(direct.Appended, $"{sheet}: a blank identity was admitted by direct nomination");
        Assert.Contains("identity column", direct.Reason, StringComparison.Ordinal);

        Record($"[IDN] {sheet} blank identity refused both ways: {direct.Reason}");
    }

    // ================================================================ the composite key

    /// <summary>
    /// <c>Dependencies</c> is the schema's one COMPOSITE identity, and this is the case that proves it
    /// is compared as a tuple rather than flattened to whichever member the caller nominated.
    ///
    /// <para>Two edges between the SAME pair of nodes differing only in relation type are both
    /// legitimate and must both be accepted. An implementation that keyed on the nominated member
    /// alone would refuse the second — and, symmetrically, would admit a duplicate edge differing
    /// only in a member it was not looking at.</para>
    /// </summary>
    [Fact]
    public void Dependencies_accepts_a_second_edge_differing_only_in_relation_type()
    {
        using var fixture = new Fixture();

        var first = Values("Dependencies", "Status");
        var second = Values("Dependencies", "Status");
        second["RelationType"] = "W8DIDN-RELATION-SECOND";

        Assert.True(Append(fixture, Request("Dependencies", "SourceId", first)).Appended);

        var result = Append(fixture, Request("Dependencies", "SourceId", second));

        Assert.True(result.Appended,
            "a second edge differing only in RelationType is a distinct record, not a collision: "
            + result.Reason);

        Record($"[IDN] Dependencies accepted a second edge differing in RelationType at row {result.Row}");
    }

    /// <summary>The complement: a fully duplicated triple is a collision.</summary>
    [Fact]
    public void Dependencies_refuses_a_duplicate_composite_key()
    {
        using var fixture = new Fixture();

        Assert.True(Append(fixture, Request("Dependencies", "SourceId", Values("Dependencies", "Status"))).Appended);

        var result = Append(fixture, Request("Dependencies", "SourceId", Values("Dependencies", "Status")));

        Assert.False(result.Appended);
        Assert.Contains("already exists", result.Reason, StringComparison.Ordinal);

        Record($"[IDN] Dependencies refused a duplicate triple: {result.Reason}");
    }

    /// <summary>
    /// A half-supplied composite key is refused. Identity that is only partly present cannot be
    /// checked for collision, so accepting it would reopen defect 1 through the back door: supply the
    /// members you are willing to have compared, omit the one you are not.
    /// </summary>
    [Fact]
    public void Dependencies_refuses_a_half_supplied_composite_key()
    {
        using var fixture = new Fixture();

        var values = Values("Dependencies", "Status");
        values.Remove("RelationType");

        var result = Append(fixture, Request("Dependencies", "SourceId", values));

        Assert.False(result.Appended);
        Assert.Contains("identity column", result.Reason, StringComparison.Ordinal);

        Record($"[IDN] Dependencies refused a half-supplied key: {result.Reason}");
    }

    // ================================================================ defect 3 — change id vs declared scope

    /// <summary>
    /// Defect 3: a record stamped with one change id under a scope authorising another is refused.
    ///
    /// <para>The envelope records <c>ChangeId</c> as the origin of the record, and the declared scope
    /// is what authorised the write. If they disagree the record names an origin that never
    /// authorised it, and the audit trail says something untrue about where the row came from. The
    /// returned verdict is <c>ScopeAmendmentRequired</c>, the component's existing vocabulary for
    /// "the declaration does not cover this record".</para>
    /// </summary>
    [Fact]
    public void Append_refuses_a_record_whose_change_id_differs_from_its_declared_scope()
    {
        using var fixture = new Fixture();
        var before = fixture.Sha256();

        var request = Request("GitLineage", "LineageId", Values("GitLineage", "Branch"))
            with { ChangeId = "CHG-W8D-SOMEONE-ELSES-0001" };

        var result = Append(fixture, request);

        Assert.False(result.Appended);
        Assert.Contains("SCOPE_CHANGE_REQUIRED", result.Reason, StringComparison.Ordinal);
        Assert.Equal(ChangeScopeContainmentVerdict.ScopeAmendmentRequired, result.Containment);
        Assert.Equal(before, fixture.Sha256());

        // The twin: the identical request under the change its scope declares succeeds. Without this
        // the refusal above could be satisfied by anything, including a scope that covers nothing.
        using var control = new Fixture();
        Assert.True(Append(control, Request("GitLineage", "LineageId", Values("GitLineage", "Branch"))).Appended);

        Record($"[IDN] mismatched change id refused: {result.Reason}");
    }

    // ================================================================ not an append target

    /// <summary>
    /// A bound sheet with no business identity is not an append target, and saying so is part of the
    /// fix: the alternative is a caller-guessed key on a sheet whose every column is governance
    /// envelope.
    ///
    /// <para><c>ExistingAssets</c> resolves to 14 envelope columns and nothing else. A caller can
    /// nominate any name it likes and it will be an envelope column, so the envelope guard refuses it
    /// either way — which means this case does NOT discriminate between the fixed and unfixed
    /// implementations. It is kept because the contract it states ("this sheet cannot be appended
    /// to") is the one the schema now enforces directly, and a failure here would mean the envelope
    /// guard had been weakened.</para>
    /// </summary>
    [Fact]
    public void A_sheet_with_no_business_identity_is_not_an_append_target()
    {
        using var fixture = new Fixture();
        var before = fixture.Sha256();

        var result = Append(fixture, Request(
            "ExistingAssets",
            "RepositoryId",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["RepositoryId"] = "W8DIDN-REPO" }));

        Assert.False(result.Appended);
        Assert.Equal(before, fixture.Sha256());

        Record($"[IDN] ExistingAssets refused: {result.Reason}");
    }

    // ================================================================ plumbing

    private static string[] CoveredTargets() =>
        AppendTargets().Select(row => (string)row[0]!).ToArray();

    /// <summary>
    /// A well-formed record for <paramref name="sheet"/>: every member of its identity (all of them,
    /// for a composite), plus <paramref name="nonKey"/> so the negative cases nominate a column that
    /// RESOLVES. The identity values are per-sheet so a case that appends twice cannot collide with an
    /// unrelated row the candidate already carries.
    /// </summary>
    private static Dictionary<string, string> Values(string sheet, string nonKey)
    {
        var binding = WorkbookCompatibilityMap.Sheets
            .Single(b => string.Equals(b.LogicalName, sheet, StringComparison.Ordinal));

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in binding.V3ImmutableIdentity!)
            values[column] = $"W8DIDN-{column.ToUpperInvariant()}-{sheet.ToUpperInvariant()}";

        values[nonKey] = $"W8DIDN-NONKEY-{sheet.ToUpperInvariant()}";
        return values;
    }

    private static DevelopmentControlAppendRecord Request(
        string sheet, string identityColumn, Dictionary<string, string> values) =>
        new(sheet,
            identityColumn,
            Change,
            values,
            DevelopmentControlProvenance.Native(DateTimeOffset.UtcNow, "w8d-identity-suite"),
            ScopeFor(Change));

    private static ChangeScopeDeclaration ScopeFor(string changeId) =>
        new("w8d-identity-lane", changeId,
            [new ChangeScopeItem(ChangeScopeItemKind.ControlStore,
                ChangeScopeAccessMode.Write, "NEXUS_DEVELOPMENT_CONTROL.xlsx")]);

    private static DevelopmentControlAppendResult Append(Fixture fixture, DevelopmentControlAppendRecord request)
    {
        using var reservation = new DevelopmentControlLockService()
            .TryAcquire(fixture.Copy, owner: "w8d-identity-suite").Reservation
            ?? throw new InvalidOperationException("the fixture lock was not acquired");

        return new DevelopmentControlWriterAuthorizer().Append(reservation, request);
    }

    private static readonly object EvidenceGate = new();

    private static void Record(string line)
    {
        var path = Environment.GetEnvironmentVariable("W1_T10_EVIDENCE");
        if (string.IsNullOrWhiteSpace(path)) return;

        lock (EvidenceGate) File.AppendAllText(path, line + Environment.NewLine);
    }

    /// <summary>
    /// A throwaway copy of the candidate. Disposable by construction: the authoritative workbook is
    /// never the subject of a mutation test, so a refusal is never demonstrated by damaging one.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Copy { get; }
        public string LockDir { get; }

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "w8d-identity-" + Guid.NewGuid().ToString("N")[..10]);
            Directory.CreateDirectory(Root);
            Copy = Path.Combine(Root, "NEXUS_DEVELOPMENT_CONTROL.xlsx");
            File.Copy(CandidateWorkbook, Copy, overwrite: false);
            // The CANONICAL lock directory: beside the workbook it guards, derived from it. The fixture
            // used to name its own directory, which the hardening now refuses.
            LockDir = AtomicWriterLock.CanonicalLockDirectoryFor(Copy);
            Directory.CreateDirectory(LockDir);
        }

        public string Sha256() => WorkbookCompatibilityReader.Sha256Of(Copy).ToUpperInvariant();

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* temp; best effort */ }
        }
    }
}
