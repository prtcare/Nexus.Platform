using Nexus.DevelopmentControl.Safety;
using Xunit;

namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// The <c>lineage-completeness</c> subcheck of <see cref="ControlHealthService.Evaluate"/> — the
/// absent-input hole, and the three-way branch that closes it.
///
/// <para><b>What was wrong.</b> The subcheck took two nullable halves,
/// <see cref="ControlHealthInputs.ReservationsMissingBaseline"/> and
/// <see cref="ControlHealthInputs.LineageGaps"/>, and substituted
/// <c>Array.Empty&lt;string&gt;()</c> for whichever half was <c>null</c>. Both halves empty therefore
/// produced the check's STRONGEST possible claim — "Every reservation carries a BaseSHA and every
/// governed record is bound." A caller that had measured reservation baselines but never computed
/// lineage (which is exactly what Forge's own health service did) was handed a green over the
/// surface it had not measured. The input was absent; the answer was affirmative.</para>
///
/// <para><b>Why this is worse than a missing metric.</b> <c>lineage-completeness</c> is the only
/// subcheck in this report whose subject is whether work is <i>reconstructable</i>. A green there is
/// consumed downstream as "the lineage is complete", so the failure mode is not a stale dashboard —
/// it is a decision made on evidence nobody gathered. The whole point of
/// <see cref="HealthStatus.Unknown"/> existing in this type is that an unmeasured surface must never
/// collapse into a passing one, and this subcheck was the one place that rule was broken.</para>
///
/// <para><b>What is proved here.</b> Four cases, one per branch of the fix: absent half (never
/// green), both supplied and empty (green — the positive control that keeps the guard from being
/// over-broad), both absent (not measured), and a real finding in one half (DEGRADED, so the new
/// UNKNOWN branch cannot swallow an actual defect into a non-answer).</para>
///
/// <para><b>The negative control is <see cref="MissingLineageInput_IsNeverReportedGreen"/>.</b> It is
/// written so that it FAILS against the pre-fix code: the rest of the inputs are a fully healthy
/// baseline, so the old implementation produced <c>Healthy</c> both for the subcheck and for the
/// aggregate, and the assertions here reject exactly that.</para>
/// </summary>
public sealed class ControlHealthLineageTests
{
    private const string LineageCheck = "lineage-completeness";

    /// <summary>A fixed instant, injected so nothing about this file depends on the wall clock.</summary>
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The measured, passing workbook schema. <see cref="ReaderResult.Supported"/> with no sheets
    /// yields zero records, which is the shape the <c>schema-recognised</c> and
    /// <c>compatibility-reader</c> subchecks both read as OK.
    /// </summary>
    private static WorkbookReadResult SupportedSchema() => new(
        Result: ReaderResult.Supported,
        Form: WorkbookForm.V3,
        SourcePath: @"D:\NEXUS\DevelopmentControl\NEXUS_DEVELOPMENT_CONTROL.xlsx",
        SourceSha256: new string('a', 64),
        MatchesFrozenHash: true,
        Sheets: Array.Empty<SheetRead>(),
        Diagnostics: Array.Empty<string>(),
        SharedStringCount: 0,
        LiteralStringCount: 0,
        UnboundSheets: Array.Empty<string>(),
        GovernanceSubstrateAbsent: Array.Empty<string>());

    /// <summary>
    /// Every other subcheck measured and passing, so that the aggregate is determined SOLELY by the
    /// lineage subcheck under test. Without this baseline the aggregate is <c>Unknown</c> for nine
    /// unrelated reasons and the aggregate assertion would be true no matter what the lineage
    /// subcheck did — the classic way a green is proved by the wrong thing.
    /// </summary>
    private static ControlHealthInputs HealthyBaseline() => new()
    {
        StoreReadable = true,
        WorkbookSchema = SupportedSchema(),
        LockSystemFunctioning = true,
        Reservations = Array.Empty<ReservationLease>(),
        Now = FixedNow,
        DependencyEdges = new Dictionary<string, IReadOnlyList<string>>(),
        ReservationConflicts = Array.Empty<string>(),
        ReservationsMissingBaseline = Array.Empty<string>(),
        LineageGaps = Array.Empty<string>(),
    };

    private static HealthCheck Check(ControlHealthReport report, string name) =>
        report.Checks.Single(c => c.Name == name);

    // ------------------------------------------------------------------ 1. the negative control

    /// <summary>
    /// <b>The load-bearing test.</b> Reservation baselines were measured and are clean; lineage was
    /// never computed. The check must NOT report green, and neither may the aggregate.
    ///
    /// <para>Against the pre-fix code both assertions fail: the absent half was replaced with an
    /// empty array, both halves came out empty, and the subcheck returned
    /// <c>HealthCheck.Ok("lineage-completeness", "Every reservation carries a BaseSHA and every
    /// governed record is bound.")</c> — the strongest claim the check can make, made over a surface
    /// nobody measured. Because the rest of the baseline is healthy, that OK also drove the
    /// aggregate to <c>Healthy</c>, so this test is sensitive to the defect at BOTH levels.</para>
    /// </summary>
    [Fact]
    public void MissingLineageInput_IsNeverReportedGreen()
    {
        // Everything measured and passing EXCEPT LineageGaps, which is deliberately left null.
        var inputs = HealthyBaseline() with { LineageGaps = null };

        var report = ControlHealthService.Evaluate(inputs);
        var check = Check(report, LineageCheck);

        Assert.NotEqual(HealthStatus.Healthy, check.Status);
        Assert.Equal(HealthStatus.Unknown, check.Status);
        Assert.NotEqual(HealthStatus.Healthy, report.Aggregate);

        // The reason must say which half was missing and that the result is UNKNOWN — a caller that
        // only sees "Unknown" cannot tell an absent input from a failing one.
        Assert.Contains("was not supplied", check.Detail, StringComparison.Ordinal);
        Assert.Contains("UNKNOWN", check.Detail, StringComparison.Ordinal);
        Assert.Contains("lineage-binding completeness", check.Detail, StringComparison.Ordinal);

        // The subcheck must not make the completeness claim it cannot support.
        Assert.DoesNotContain("every governed record is bound", check.Detail, StringComparison.Ordinal);

        // ... and the report must not be green by any route, including IsGreen.
        Assert.False(report.IsGreen);
        Assert.Contains(check, report.Blockers);
    }

    /// <summary>
    /// The same defect with nothing else supplied at all — the literal "one half supplied, the other
    /// null" shape, with no healthy baseline to hide behind. Kept separate from the control above so
    /// that the absent-input rule is proved independently of the rest of the report.
    /// </summary>
    [Fact]
    public void OnlyOneHalfSupplied_IsNeverReportedGreen_InEitherDirection()
    {
        var baselineOnly = ControlHealthService.Evaluate(new ControlHealthInputs
        {
            ReservationsMissingBaseline = Array.Empty<string>(),
            LineageGaps = null,
        });
        var lineageOnly = ControlHealthService.Evaluate(new ControlHealthInputs
        {
            ReservationsMissingBaseline = null,
            LineageGaps = Array.Empty<string>(),
        });

        foreach (var report in new[] { baselineOnly, lineageOnly })
        {
            var check = Check(report, LineageCheck);
            Assert.Equal(HealthStatus.Unknown, check.Status);
            Assert.NotEqual(HealthStatus.Healthy, report.Aggregate);
            Assert.Contains("was not supplied", check.Detail, StringComparison.Ordinal);
            Assert.Contains("UNKNOWN", check.Detail, StringComparison.Ordinal);
        }

        // Each direction names the half that was actually absent, so the two are distinguishable.
        Assert.Contains("lineage-binding completeness", Check(baselineOnly, LineageCheck).Detail, StringComparison.Ordinal);
        Assert.Contains("reservation-baseline completeness", Check(lineageOnly, LineageCheck).Detail, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ 2. the positive control

    /// <summary>
    /// Both halves supplied, both empty: the completeness claim IS warranted, and the check says so.
    ///
    /// <para>This exists to keep the guard above from being proved by being over-broad. A rule of
    /// "never report lineage green" would satisfy
    /// <see cref="MissingLineageInput_IsNeverReportedGreen"/> while destroying the subcheck's
    /// ability to ever pass — the report would be permanently UNKNOWN and the check would carry no
    /// information. Asserting the green here is what makes the UNKNOWN there mean "unmeasured"
    /// rather than "unmeasurable".</para>
    ///
    /// <para>The aggregate is asserted too: with every subcheck measured and passing the whole
    /// report is <c>Healthy</c>, which is the state the pre-fix code reached FOR THE WRONG REASON in
    /// the control above.</para>
    /// </summary>
    [Fact]
    public void BothSuppliedAndEmpty_IsGreen()
    {
        var report = ControlHealthService.Evaluate(HealthyBaseline());
        var check = Check(report, LineageCheck);

        Assert.Equal(HealthStatus.Healthy, check.Status);
        Assert.Equal("Every reservation carries a BaseSHA and every governed record is bound.", check.Detail);

        Assert.Equal(HealthStatus.Healthy, report.Aggregate);
        Assert.True(report.IsGreen);
        Assert.Empty(report.Blockers);
    }

    // ------------------------------------------------------------------ 3. both absent

    /// <summary>
    /// Neither half supplied: there is no measured half to report on, so the reason must be the
    /// plain "not measured" one rather than the per-half wording. UNKNOWN, never green.
    /// </summary>
    [Fact]
    public void BothNull_IsNotMeasured()
    {
        var inputs = new ControlHealthInputs { LineageGaps = null, ReservationsMissingBaseline = null };

        var report = ControlHealthService.Evaluate(inputs);
        var check = Check(report, LineageCheck);

        Assert.Equal(HealthStatus.Unknown, check.Status);
        Assert.NotEqual(HealthStatus.Healthy, report.Aggregate);
        Assert.Contains("not measured", check.Detail, StringComparison.OrdinalIgnoreCase);

        // The both-absent branch must be distinguishable from the one-half-absent branch; if it were
        // not, a caller could not tell "nothing was measured" from "half was measured and clean".
        Assert.DoesNotContain("was not supplied", check.Detail, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ 4. a real finding still surfaces

    /// <summary>
    /// A real lineage gap with the reservation half measured and clean must be DEGRADED — not
    /// swallowed into the new UNKNOWN branch.
    ///
    /// <para>This is the guard on the fix itself. The cheapest way to stop reporting a false green
    /// would have been to widen the absent-input branch until it also caught findings, which would
    /// have traded a silent pass for a silent non-answer and hidden the very defects the subcheck
    /// exists to report. DEGRADED outranks UNKNOWN in
    /// <see cref="ControlHealthService.Derive"/>, so the finding also survives into the aggregate —
    /// asserted here, because that is the property that makes it visible to a consumer that reads
    /// only the aggregate.</para>
    /// </summary>
    [Fact]
    public void NonEmptyGaps_IsDegraded()
    {
        var inputs = HealthyBaseline() with { LineageGaps = new[] { "ChangeId=W8D-R4-0007", "ChangeId=W8D-R4-0011" } };

        var report = ControlHealthService.Evaluate(inputs);
        var check = Check(report, LineageCheck);

        Assert.Equal(HealthStatus.Degraded, check.Status);
        Assert.NotEqual(HealthStatus.Unknown, check.Status);

        // The finding names the records it could not bind, rather than only counting them.
        Assert.Contains("2 record(s) cannot be bound to a baseline", check.Detail, StringComparison.Ordinal);
        Assert.Equal(2, check.Evidence.Count);
        Assert.Contains("ChangeId=W8D-R4-0007", check.Evidence);
        Assert.Contains("ChangeId=W8D-R4-0011", check.Evidence);

        Assert.Equal(HealthStatus.Degraded, report.Aggregate);
        Assert.Contains(check, report.Blockers);
    }

    /// <summary>The mirror of the case above: the same finding in the OTHER half. A fix that only
    /// handled one direction would leave the other half silently green.</summary>
    [Fact]
    public void NonEmptyReservationsMissingBaseline_IsDegraded()
    {
        var inputs = HealthyBaseline() with { ReservationsMissingBaseline = new[] { "RSV-0001" } };

        var report = ControlHealthService.Evaluate(inputs);
        var check = Check(report, LineageCheck);

        Assert.Equal(HealthStatus.Degraded, check.Status);
        Assert.Contains("1 reservation(s) carry no BaseSHA", check.Detail, StringComparison.Ordinal);
        Assert.Equal(HealthStatus.Degraded, report.Aggregate);
    }
}
