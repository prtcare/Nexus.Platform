using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Nexus.DevelopmentControl.Safety;
using Nexus.ProductCore.Contracts.ReadModel;
using Nexus.ProductCore.Core.ReadModel;
using Xunit;

namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// W10.5B — <b>the Governance Gate Registry binding, and the twelve failure controls that decide
/// whether it is safe to render.</b>
///
/// <para>
/// <b>What the authority is, because everything below depends on it.</b> Measured six independent
/// ways and ratified by the Owner: <c>18_Governance</c> is a GOVERNANCE GATE REGISTRY, not a decision
/// ledger. Its <c>Status</c> column carries planning vocabulary (<c>Proposed</c>, <c>Not Started</c>,
/// <c>Planned</c>, <c>Superseded</c>) and never a verdict; <c>AuthorityProfile</c> is the constant
/// <c>PLATFORM_AUTHORITY</c>; and <c>GateId</c>, <c>ChangeId</c> and <c>SupersedesVersion</c> are
/// blank on every row, which is why the registry cannot be joined to anything.
/// </para>
///
/// <para>
/// <b>Every control below is paired with something that would fail if it were asserted rather than
/// measured.</b> A test that only proves "a missing sheet does not produce gates" passes just as well
/// against an implementation that never produces gates at all, so each absence is asserted beside the
/// presence it is distinguished from.
/// </para>
///
/// <para>
/// <b>Fixtures are generated workbooks, never the live estate.</b> The real registry holds 91 live
/// governance records; committing one would move estate data into a public repository to satisfy a
/// test. The fixture is built from the same binding table the production reader resolves against, so
/// a binding written against the wrong header names fails here rather than in production.
/// </para>
/// </summary>
public sealed class GovernanceGateRegistryTests : IDisposable
{
    private const string ObservedAt = "2026-10-07T00:00:00Z";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "w105b-governance-" + Guid.NewGuid().ToString("N")[..12]);

    public GovernanceGateRegistryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // An inert temp directory is not a test failure.
        }
    }

    private string WorkbookPath => Path.Combine(_root, "NEXUS_DEVELOPMENT_CONTROL.xlsx");
    private string Destination => Path.Combine(_root, "published");

    private DevelopmentControlReadPublisher Publisher() =>
        new(new DevelopmentControlReadProjection(WorkbookPath));

    private DevelopmentControlPublishOutcome Publish() => Publisher().Publish(Destination, ObservedAt);

    private DevelopmentControlReadPayload Project()
    {
        using var stream = File.OpenRead(Path.Combine(Destination, DevelopmentControlReadContract.FileName));
        using var document = JsonDocument.Parse(stream);

        return JsonSerializer.Deserialize<DevelopmentControlReadModel>(
            document.RootElement.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Payload;
    }

    // ================================================================ the binding

    /// <summary>
    /// The sheet is BOUND, and it is no longer declared unbound.
    ///
    /// <para>
    /// The two assertions are one claim seen from both sides: <c>KnownUnboundV3Sheets</c> is a
    /// statement that the named sheets cannot be read through the contract, so a bound sheet left on
    /// it makes the list false in the direction that matters. Asserting only the binding would leave
    /// that contradiction in place.
    /// </para>
    /// </summary>
    [Fact]
    public void The_registry_sheet_is_bound_and_no_longer_declared_unbound()
    {
        var binding = WorkbookCompatibilityMap.Binding("GovernanceGates");

        Assert.NotNull(binding);
        Assert.Equal("18_Governance", binding!.V3Name);
        Assert.Equal(4, WorkbookCompatibilityMap.HeaderRowFor(binding, WorkbookForm.V3));

        Assert.DoesNotContain("18_Governance", WorkbookCompatibilityMap.KnownUnboundV3Sheets);

        // ...and the scope Owner decision 3 drew is intact: the other two surfaces stay unprojected.
        Assert.Contains("21_Risks", WorkbookCompatibilityMap.KnownUnboundV3Sheets);
        Assert.Contains("22_Exceptions", WorkbookCompatibilityMap.KnownUnboundV3Sheets);

        // READ-ONLY. No declared identity means no append target, so the binding cannot have handed
        // any host a write path into a governance surface as a side effect of being able to show it.
        Assert.True(binding.V3ImmutableIdentity is null or { Count: 0 },
            "the Governance Gate Registry is bound for READING; declaring an identity would make it an append target");
    }

    /// <summary>
    /// The 22 headers the binding declares against the sheet are exactly the 22 the authority carries,
    /// A..V, in the same order.
    ///
    /// <para>
    /// This is the check that makes the header list evidence rather than transcription. The authority
    /// was re-measured for this milestone; the expected sequence below is that measurement, written
    /// out once so a future edit to the binding has to disagree with it out loud.
    /// </para>
    /// </summary>
    [Fact]
    public void The_binding_declares_the_twenty_two_columns_the_authority_carries()
    {
        var expected = new[]
        {
            "GovernanceId", "GateId", "Name", "AuthorityProfile", "RequiredEvidence", "RegistryStatus",
            "BlocksScope", "Notes", "RecordVersion", "IsCurrent", "EffectiveFrom", "EnvelopeChangeId",
            "SupersedesVersion", "SourceForm", "SourceWorkbook", "SourceWorkbookHash", "SourceSheet",
            "SourceRecordId", "SourceRevision", "SourceArchitectureVersion", "MigrationTimestamp",
            "MigrationTransformation",
        };

        var bound = WorkbookCompatibilityMap.ColumnsFor("GovernanceGates");
        var explicitNames = bound.Select(c => c.LogicalName).ToList();

        // The nine envelope columns are supplied by the cross-sheet projection rather than bound here,
        // exactly as they are on every other V3 sheet — so the declaration is checked in the order the
        // fixture and the sheet both lay out: bound columns first, then the envelope the projection adds.
        Assert.Equal(expected[..13], explicitNames);

        var projected = WorkbookCompatibilityMap.EnvelopeProjection
            .Where(p => !bound.Any(c => c.LogicalName == p.LogicalName))
            .Select(p => p.LogicalName);

        Assert.Equal(expected[13..], projected);

        // The contract-side rename is the safety property, so it is asserted rather than assumed:
        // no member of the registry's declared surface is named `Status`.
        Assert.DoesNotContain("Status", explicitNames);
        Assert.Contains("RegistryStatus", explicitNames);
    }

    // ================================================================ control 1 — missing is not empty

    /// <summary>
    /// <b>Control 1: a missing <c>18_Governance</c> is NOT a valid empty registry.</b>
    ///
    /// <para>
    /// The workbook still reads as V3 — the form is identified by three other sheets — and the binding
    /// still DECLARES the registry, so the reader reports <c>DECLARED_BUT_MISSING</c> rather than
    /// silence. The projection must turn that into <c>UNAVAILABLE</c>. Publishing
    /// <c>AVAILABLE</c> with zero gates here would assert that the control governs nothing, which is an
    /// answer to a question this workbook was never able to answer.
    /// </para>
    /// </summary>
    [Fact]
    public void A_missing_registry_sheet_is_UNAVAILABLE_and_not_a_registry_with_no_gates()
    {
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath, [], omitGovernanceSheet: true);

        var outcome = Publish();
        Assert.True(outcome.Published, outcome.Reason);

        var payload = Project();
        var registry = payload.Governance;

        Assert.Equal(DevelopmentControlReadGovernanceRegistry.Unavailable, registry.State);
        Assert.Empty(registry.Gates);

        // The detail must say WHICH absence this is, so an operator is not left guessing between a
        // missing sheet, an unreadable one and a wrong form.
        Assert.Contains("18_Governance", registry.Detail, StringComparison.Ordinal);

        // ...and the sheet really was absent from the container, rather than the fixture having
        // silently built it and the test measuring something else.
        using var zip = ZipFile.OpenRead(WorkbookPath);
        var names = SheetNames(zip);
        Assert.DoesNotContain("18_Governance", names);
        Assert.Contains("07_WorkItems", names); // the control: the workbook is otherwise a real V3
    }

    // ================================================================ control 2 — a real zero is explicit

    /// <summary>
    /// <b>Control 2: a genuinely empty but READABLE registry is an explicit zero.</b>
    ///
    /// <para>
    /// This is the other half of control 1 and the reason neither assertion is meaningful alone. Here
    /// the sheet is present with its header row and no data rows: the source answered, and the answer
    /// is zero. The two cases differ by <c>State</c> and by nothing else in the gate list, which is
    /// exactly why <c>State</c> has to be in the document.
    /// </para>
    /// </summary>
    [Fact]
    public void A_readable_registry_with_no_rows_is_an_explicit_zero_and_is_distinguishable_from_control_1()
    {
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath, []);

        Assert.True(Publish().Published);

        var zero = Project().Governance;

        Assert.Equal(DevelopmentControlReadGovernanceRegistry.Available, zero.State);
        Assert.Empty(zero.Gates);
        Assert.Contains("0 registry record(s)", zero.Detail, StringComparison.Ordinal);

        // The control test proves the two states are not the same value, rather than assuming it.
        Assert.NotEqual(DevelopmentControlReadGovernanceRegistry.Unavailable, zero.State);
    }

    // ================================================================ control 3 — schema mismatch refuses

    /// <summary>
    /// <b>Control 3: a schema mismatch refuses.</b>
    ///
    /// <para>
    /// The identity column is RENAMED rather than deleted, and the distinction is the whole test: a
    /// renamed column occupies the same position and holds the same kind of value, so a positional
    /// reader would find it and never notice. The required-column rule is what refuses it, the reader
    /// reports <c>UNSUPPORTED_SCHEMA</c>, and the projection refuses to publish rather than build a
    /// document from a read it could not recognise.
    /// </para>
    /// </summary>
    [Fact]
    public void A_renamed_identity_column_refuses_the_read_and_the_publication()
    {
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(
            WorkbookPath,
            [new WorkbookFixtureBuilder.GovernanceRow("GOV-1", "Proposed", "A gate")],
            renameGovernanceIdentityColumn: true);

        // The reader names the missing column and the sheet it was expected on.
        var read = WorkbookCompatibilityReader.Read(WorkbookPath);
        Assert.Equal(ReaderResult.UnsupportedSchema, read.Result);
        Assert.Contains(read.Diagnostics, d =>
            d.Contains("UNSUPPORTED_SCHEMA", StringComparison.Ordinal)
            && d.Contains("GovernanceId", StringComparison.Ordinal));

        var outcome = Publish();
        Assert.False(outcome.Published);
        Assert.Contains("AUTHORITY_UNAVAILABLE", outcome.Reason, StringComparison.Ordinal);

        // Nothing was published, so no consumer can read a registry out of a read that failed.
        Assert.False(File.Exists(Path.Combine(Destination, DevelopmentControlReadContract.FileName)));
    }

    // ================================================================ controls 4 and 5 — identity

    /// <summary>
    /// <b>Control 4: a duplicated <c>GovernanceId</c> refuses the publication.</b>
    ///
    /// <para>
    /// Refused rather than de-duplicated or first-wins, because either repair produces a document
    /// whose gate count is confidently wrong — and the count is what a consumer acts on. The refusal
    /// names both rows so the duplicate is findable in the authority.
    /// </para>
    /// </summary>
    [Fact]
    public void A_duplicated_GovernanceId_refuses_the_publication()
    {
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath,
        [
            new WorkbookFixtureBuilder.GovernanceRow("GOV-DUP", "Proposed", "First"),
            new WorkbookFixtureBuilder.GovernanceRow("GOV-DUP", "Planned", "Second"),
        ]);

        var outcome = Publish();

        Assert.False(outcome.Published);
        Assert.Contains("GOVERNANCE_REGISTRY_INVALID", outcome.Reason, StringComparison.Ordinal);
        Assert.Contains("GOV-DUP", outcome.Reason, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Destination, DevelopmentControlReadContract.FileName)));

        // Positive control: the same fixture with the duplicate resolved publishes. Without this the
        // refusal above could be caused by anything about the fixture rather than by the duplicate.
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath,
        [
            new WorkbookFixtureBuilder.GovernanceRow("GOV-DUP", "Proposed", "First"),
            new WorkbookFixtureBuilder.GovernanceRow("GOV-OTHER", "Planned", "Second"),
        ]);

        Assert.True(Publish().Published, "the de-duplicated variant must publish");
    }

    /// <summary>
    /// <b>Control 5: a required blank <c>GovernanceId</c> refuses the publication.</b>
    ///
    /// <para>
    /// The row carries a Name, so it is a real record with a real gap rather than a blank row the
    /// reader would have skipped — which is what makes this a test of the identity rule rather than of
    /// row-skipping. Skipping it instead would publish a gate count one lower than the authority's.
    /// </para>
    /// </summary>
    [Fact]
    public void A_blank_GovernanceId_refuses_the_publication()
    {
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath,
        [
            new WorkbookFixtureBuilder.GovernanceRow("GOV-OK", "Proposed", "A well formed record"),
            new WorkbookFixtureBuilder.GovernanceRow("", "Planned", "A record with no identity"),
        ]);

        var outcome = Publish();

        Assert.False(outcome.Published);
        Assert.Contains("GOVERNANCE_REGISTRY_INVALID", outcome.Reason, StringComparison.Ordinal);
        Assert.Contains("no GovernanceId", outcome.Reason, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Destination, DevelopmentControlReadContract.FileName)));
    }

    // ================================================================ control 7 — status is not a verdict

    /// <summary>
    /// <b>Control 7: registry Status cannot become a governance verdict.</b>
    ///
    /// <para>
    /// Asserted structurally, in three independent ways, because a value-level assertion alone would
    /// not survive someone later adding a <c>Verdict</c> member and populating it from the same
    /// column:
    /// </para>
    /// <list type="number">
    /// <item><description>The published value is the RAW authority text, un-normalized — including a
    /// value the authority does not use, so the assertion cannot be satisfied by a mapper that only
    /// recognises the four measured planning states.</description></item>
    /// <item><description>No member of the gate record's declared surface is named after a
    /// verdict. This is the guard that fails the day someone adds one.</description></item>
    /// <item><description>The published JSON carries no verdict-shaped property anywhere in the
    /// registry section.</description></item>
    /// </list>
    /// </summary>
    [Fact]
    public void Registry_status_is_carried_raw_and_cannot_become_a_governance_verdict()
    {
        // `APPROVED` is deliberately NOT a value this authority uses. If it round-trips unchanged, the
        // projection is not mapping anything onto an invented vocabulary — which is the failure the
        // Owner's decision names, and the one a narrow allow-list would have hidden.
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath,
        [
            new WorkbookFixtureBuilder.GovernanceRow("GOV-1", "Proposed", "Planned work"),
            new WorkbookFixtureBuilder.GovernanceRow("GOV-2", "APPROVED", "A value the authority never writes"),
        ]);

        Assert.True(Publish().Published);

        var gates = Project().Governance.Gates;
        Assert.Equal("Proposed", gates.Single(g => g.GovernanceId == "GOV-1").RegistryStatus);
        Assert.Equal("APPROVED", gates.Single(g => g.GovernanceId == "GOV-2").RegistryStatus);

        // (2) The structural guard. A verdict member would have to be added here to leak, and this
        // fails the moment one is. `DeliveryAuthorityClass` is allowed because it classifies HOW the
        // fact is known, not what it decides.
        var forbidden = new[]
        {
            "verdict", "decision", "approved", "refused", "deferred", "passed", "failed",
            "outcome", "resolution", "ruling", "disposition",
        };

        foreach (var member in typeof(DevelopmentControlReadGovernanceGate)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = member.Name.ToLowerInvariant();
            Assert.DoesNotContain(forbidden, token => name.Contains(token, StringComparison.Ordinal));
        }

        // ...and it holds for the nested source record too, which is the other place a member could be
        // added without touching the gate.
        foreach (var member in typeof(DevelopmentControlReadGovernanceSource)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = member.Name.ToLowerInvariant();
            Assert.DoesNotContain(forbidden, token => name.Contains(token, StringComparison.Ordinal));
        }

        // (3) The rendered document, not just the type. A member could be added to the JSON by a
        // custom converter without ever appearing on the record.
        using var stream = File.OpenRead(Path.Combine(Destination, DevelopmentControlReadContract.FileName));
        using var document = JsonDocument.Parse(stream);

        foreach (var property in EnumerateProperties(document.RootElement.GetProperty("Payload").GetProperty("Governance")))
        {
            var name = property.ToLowerInvariant();
            Assert.DoesNotContain(forbidden, token => name.Contains(token, StringComparison.Ordinal));
        }
    }

    // ================================================================ control 8 — history is absent

    /// <summary>
    /// <b>Control 8: missing evaluator history is an explicit SOURCE_GAP.</b>
    ///
    /// <para>
    /// Emitted on every publication, including one whose registry is empty — because the absence is a
    /// property of the Governance component, not of these rows. A consumer asking what governance
    /// decided about a gate gets "nothing is recorded" whatever the registry holds, and suppressing
    /// the statement for the empty case would let a view render "no decisions" for a source that has
    /// no decision history at all.
    /// </para>
    /// </summary>
    [Fact]
    public void Missing_evaluator_decision_history_is_an_explicit_source_gap()
    {
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath,
            [new WorkbookFixtureBuilder.GovernanceRow("GOV-1", "Proposed", "A gate")]);

        Assert.True(Publish().Published);

        var gap = Assert.Single(Project().Gaps, g =>
            g.Kind == DevelopmentControlReadGapKinds.GovernanceEvaluationHistorySourceGap);

        Assert.Equal(DevelopmentControlReadGapKinds.GovernanceRegistrySubject, gap.Subject);

        // The message has to say it is an ABSENCE and not a zero, because the two render differently
        // and only one of them is the truth here.
        Assert.Contains("ABSENCE", gap.Detail, StringComparison.Ordinal);

        // And it does not degenerate into a silent empty list: the registry itself is still present.
        Assert.Equal(DevelopmentControlReadGovernanceRegistry.Available, Project().Governance.State);

        // Control: the gap is emitted for the EMPTY registry too, so it is not a function of the rows.
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath, []);
        Assert.True(Publish().Published);
        Assert.Single(Project().Gaps, g =>
            g.Kind == DevelopmentControlReadGapKinds.GovernanceEvaluationHistorySourceGap);
    }

    // ================================================================ control 9 — no cross-source join

    /// <summary>
    /// <b>Control 9: a missing Governance cross-domain relation is an explicit SOURCE_GAP — and it is
    /// MEASURED, not asserted.</b>
    ///
    /// <para>
    /// The second half is what makes this test worth writing. A gap hardcoded into the projection
    /// would pass the first assertion and be wrong the day a gate carried an identifier. The positive
    /// control below gives a gate a <c>GateId</c> and requires the gap to DISAPPEAR, which is only
    /// possible if something actually looked at the rows.
    /// </para>
    /// </summary>
    [Fact]
    public void A_registry_with_no_shared_identifier_reports_the_cross_source_join_gap()
    {
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath,
        [
            new WorkbookFixtureBuilder.GovernanceRow("GOV-1", "Proposed", "First gate"),
            new WorkbookFixtureBuilder.GovernanceRow("GOV-2", "Planned", "Second gate"),
        ]);

        Assert.True(Publish().Published);

        var payload = Project();
        var gap = Assert.Single(payload.Gaps, g =>
            g.Kind == DevelopmentControlReadGapKinds.GovernanceCrossSourceJoinGap);

        Assert.Equal(DevelopmentControlReadGapKinds.GovernanceRegistrySubject, gap.Subject);
        Assert.Contains("2 registry record(s)", gap.Detail, StringComparison.Ordinal);

        // The forbidden substitutes are named in the gap itself, so a consumer reading only the
        // document knows what was NOT used as a key.
        Assert.Contains("BlocksScope", gap.Detail, StringComparison.Ordinal);

        // The fields the claim is about are published and blank — a reader can re-measure the gap
        // rather than take it on trust.
        Assert.All(payload.Governance.Gates, g =>
        {
            Assert.Empty(g.GateId);
            Assert.Empty(g.EnvelopeChangeId);
            Assert.Empty(g.SupersedesVersion);
        });

        // ------------------------------------------------------------------ the positive control
        //
        // One gate carries a GateId. The gap must vanish — which it can only do if the projection
        // inspected the records. Without this, the assertion above is satisfied by a constant.
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath,
        [
            new WorkbookFixtureBuilder.GovernanceRow("GOV-1", "Proposed", "First gate", GateId: "GATE-1"),
            new WorkbookFixtureBuilder.GovernanceRow("GOV-2", "Planned", "Second gate"),
        ]);

        Assert.True(Publish().Published);
        Assert.DoesNotContain(Project().Gaps, g =>
            g.Kind == DevelopmentControlReadGapKinds.GovernanceCrossSourceJoinGap);

        // ...and it does not devolve into a relationship edge either. A shared identifier appearing is
        // a reason to reconsider the gap; it is NOT permission to draw an unresolvable relation.
        Assert.DoesNotContain(Project().LineageEdges, e =>
            e.From.Contains("GOV-", StringComparison.Ordinal) || e.To.Contains("GOV-", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>Control 9, second half:</b> a registry that could not be read reports NO join gap.
    ///
    /// <para>
    /// A measurement nobody took must not be reported as a measurement. When the sheet is missing there
    /// are no rows whose identifiers could be inspected, and "no shared identifier exists" would then be
    /// a claim about records the projection never saw. The <c>UNAVAILABLE</c> state is the stronger,
    /// honest statement.
    /// </para>
    /// </summary>
    [Fact]
    public void An_unreadable_registry_reports_no_join_gap_because_nothing_was_measured()
    {
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath, [], omitGovernanceSheet: true);

        Assert.True(Publish().Published);

        var payload = Project();
        Assert.Equal(DevelopmentControlReadGovernanceRegistry.Unavailable, payload.Governance.State);
        Assert.DoesNotContain(payload.Gaps, g =>
            g.Kind == DevelopmentControlReadGapKinds.GovernanceCrossSourceJoinGap);

        // The evaluation-history gap, by contrast, IS still reported: it is a fact about the
        // Component and holds whatever the workbook contains. The two gaps are not the same kind.
        Assert.Contains(payload.Gaps, g =>
            g.Kind == DevelopmentControlReadGapKinds.GovernanceEvaluationHistorySourceGap);
    }

    // ================================================================ determinism and read-only

    /// <summary>
    /// The projection is deterministic and the publication is idempotent: the same authority produces
    /// byte-identical bytes and the same semantic digest, and republishing does not accumulate.
    /// </summary>
    [Fact]
    public void Publishing_twice_from_the_same_authority_is_idempotent()
    {
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath,
        [
            new WorkbookFixtureBuilder.GovernanceRow("GOV-B", "Planned", "Second"),
            new WorkbookFixtureBuilder.GovernanceRow("GOV-A", "Proposed", "First"),
        ]);

        var first = Publish();
        Assert.True(first.Published);

        var path = Path.Combine(Destination, DevelopmentControlReadContract.FileName);
        var firstBytes = File.ReadAllBytes(path);

        var second = Publish();
        Assert.True(second.Published);

        Assert.Equal(first.PayloadDigest, second.PayloadDigest);
        Assert.Equal(firstBytes, File.ReadAllBytes(path));

        // Ordering is by GovernanceId, not by the order the rows happen to sit in the sheet — so the
        // digest is a function of the registry's content and not of its layout.
        var gates = Project().Governance.Gates.Select(g => g.GovernanceId).ToArray();
        Assert.Equal(["GOV-A", "GOV-B"], gates);
    }

    /// <summary>
    /// <b>The binding is read-only.</b> Publishing leaves the workbook byte-identical.
    ///
    /// <para>
    /// The reader has no write path by construction, but the projection calls it on a file the
    /// publisher was handed a path to, and "it could not have written" is weaker evidence than "it did
    /// not". The hash is taken across a full publish.
    /// </para>
    /// </summary>
    [Fact]
    public void Publishing_does_not_alter_the_authority()
    {
        WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath,
            [new WorkbookFixtureBuilder.GovernanceRow("GOV-1", "Proposed", "A gate")]);

        var before = WorkbookCompatibilityReader.Sha256Of(WorkbookPath);

        Assert.True(Publish().Published);

        Assert.Equal(before, WorkbookCompatibilityReader.Sha256Of(WorkbookPath));

        // And nothing was written BESIDE it either — no staging file, no lock, no sidecar.
        var siblings = Directory.GetFiles(Path.GetDirectoryName(WorkbookPath)!)
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Equal(["NEXUS_DEVELOPMENT_CONTROL.xlsx"], siblings);
    }

    /// <summary>
    /// <b>The registry is not an append target.</b>
    ///
    /// <para>
    /// Proved where the refusal is actually decided. The append path reads identity from the SCHEMA
    /// and never from the caller — <c>DevelopmentControlContractAdapters.Append</c> refuses as soon as
    /// <c>SheetBinding.V3ImmutableIdentity</c> is null or empty, before any column is consulted — so
    /// "no identity is declared" and "cannot be appended to" are the same fact, and asserting the
    /// first is asserting the second.
    /// </para>
    ///
    /// <para>
    /// <b>Why the refusal is not driven end to end from here.</b> Reaching that branch requires a held
    /// reservation, and the reservation is taken through <c>AtomicWriterLock</c>, whose path grammar
    /// is Windows-shaped by construction. Doing it in this class would move the whole class into the
    /// <c>WindowsOnly</c> lane and take the portable proof of the registry with it — a worse trade
    /// than citing the guard, because the append path's own drift detector
    /// (<c>DevelopmentControlAppendIdentityTests.The_schema_declares_identity_for_exactly_the_targets_this_suite_covers</c>)
    /// already fails if any sheet declares an identity its suite does not cover. That test is
    /// currently green with this binding, which is the end-to-end half of this assertion.
    /// </para>
    /// </summary>
    [Fact]
    public void The_registry_declares_no_append_identity_and_is_therefore_not_writable()
    {
        var binding = WorkbookCompatibilityMap.Binding("GovernanceGates");

        Assert.NotNull(binding);
        Assert.Null(binding!.V3ImmutableIdentity);

        // ...and no logical column of the sheet is silently standing in for one: the append path
        // compares the caller's column against the declared list, so a column present in the binding
        // is not the same as an identity and must not be mistaken for one.
        Assert.DoesNotContain("GovernanceId",
            WorkbookCompatibilityMap.Sheets
                .Where(b => b.V3ImmutableIdentity is { Count: > 0 })
                .SelectMany(b => b.V3ImmutableIdentity!));
    }

    // ================================================================ helpers

    private static string[] SheetNames(ZipArchive zip)
    {
        XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var stream = zip.GetEntry("xl/workbook.xml")!.Open();
        var document = XDocument.Load(stream);

        return document.Root!
            .Element(m + "sheets")!
            .Elements(m + "sheet")
            .Select(s => s.Attribute("name")!.Value)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var nested in EnumerateProperties(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in EnumerateProperties(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }
}
