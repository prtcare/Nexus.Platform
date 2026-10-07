using System.IO.Compression;
using System.Xml.Linq;
using Nexus.DevelopmentControl.Safety;
using Xunit;

namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// W10.5B TASK 6 — <b>a sheet's rows must never be counted with an unprefixed XML name.</b>
///
/// <para>
/// <b>The trap, reproduced before it is guarded.</b> Worksheet XML is namespaced: a row element is
/// <c>&lt;x:row&gt;</c>, and an expression matching the bare name <c>row</c> matches NOTHING. During
/// W10.5B's discovery this produced a confident, wrong answer twice — a raw count that reported
/// <b>zero rows for every sheet</b>, including <c>07_WorkItems</c>, which holds 847 of them. The
/// measurement did not fail. It returned a number, the number was zero, and zero is a plausible
/// answer for a sheet nobody has looked at yet.
/// </para>
///
/// <para>
/// <b>What exposed it was a control, not a check.</b> A count with no known-good value beside it
/// cannot tell "this sheet is empty" from "my counter is broken". Every assertion below is therefore
/// paired: the trap is demonstrated to fire, AND a sheet known to hold rows is demonstrated to report
/// them.
/// </para>
///
/// <para>
/// <b>WHY THIS IS NOT A TEST ABOUT <c>18_Governance</c>.</b> That sheet is what drew attention to the
/// problem, and guarding only it would leave the same defect available on the other twenty-five. The
/// assertions below are general: the namespace behaviour is proven for EVERY worksheet part in the
/// container, and the "rows in the container do not read as zero records" rule is applied to every
/// BOUND sheet without naming any of them.
/// </para>
///
/// <para>
/// <b>The production rule this protects.</b> Production code reads through
/// <see cref="WorkbookCompatibilityReader"/> and its column/row resolution — never through a bespoke
/// XML scan. This test reads the raw XML itself, on purpose, because the thing being checked is the
/// distance between a naive raw count and the canonical reader's answer.
/// </para>
/// </summary>
public sealed class WorkbookRowCountNamespaceTests : IDisposable
{
    private static readonly XNamespace SpreadsheetMl = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "w105b-rowcount-" + Guid.NewGuid().ToString("N")[..12]);

    public WorkbookRowCountNamespaceTests() => Directory.CreateDirectory(_root);

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

    /// <summary>
    /// A workbook with a KNOWN non-zero number of rows on a KNOWN sheet, so every count below has a
    /// value beside it that would expose a broken counter.
    /// </summary>
    private const int KnownGovernanceRows = 3;

    private void WriteFixture() => WorkbookFixtureBuilder.AuthoritativeWithGovernance(WorkbookPath,
    [
        new WorkbookFixtureBuilder.GovernanceRow("GOV-ROWCOUNT-1", "Proposed", "First"),
        new WorkbookFixtureBuilder.GovernanceRow("GOV-ROWCOUNT-2", "Planned", "Second"),
        new WorkbookFixtureBuilder.GovernanceRow("GOV-ROWCOUNT-3", "Superseded", "Third"),
    ]);

    // ================================================================ the trap, demonstrated

    /// <summary>
    /// <b>The trap, proven to be present in the fixture.</b>
    ///
    /// <para>
    /// If an unprefixed name matched anything, the guard below would be protecting against nothing and
    /// would keep passing after the namespace changed. Asserting that it matches ZERO on every part —
    /// including parts that certainly hold rows — is what makes the rest of this file mean something.
    /// </para>
    /// </summary>
    [Fact]
    public void An_unprefixed_row_name_matches_nothing_in_any_worksheet_part()
    {
        WriteFixture();

        using var zip = ZipFile.OpenRead(WorkbookPath);
        var parts = WorksheetParts(zip);

        Assert.NotEmpty(parts);

        var withRows = 0;
        foreach (var part in parts)
        {
            var document = Load(zip, part);

            // Namespace-AWARE: the real count.
            var actual = Rows(document, SpreadsheetMl).Count();

            // Namespace-UNAWARE: the trap. Zero, every time, however many rows the part holds.
            var naive = document.Root!.Element("sheetData")?.Elements("row").Count() ?? 0;

            Assert.Equal(0, naive);

            if (actual > 0)
            {
                withRows++;
            }
        }

        // The control. If no part held rows, "the naive count is 0" would be true for the trivial
        // reason and every assertion above would be vacuous.
        Assert.True(withRows > 0, "the fixture must contain worksheet parts that hold rows");
    }

    // ================================================================ the reader agrees with reality

    /// <summary>
    /// <b>A part that holds rows must not read as zero records — on EVERY bound sheet.</b>
    ///
    /// <para>
    /// Stated without naming a sheet, because the defect it guards is not specific to one. A counting
    /// path that lost the namespace, or that counted something other than rows, would report a
    /// populated sheet as empty and the workbook as valid.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_bound_sheet_the_container_holds_rows_for_reports_records()
    {
        WriteFixture();

        var read = WorkbookCompatibilityReader.Read(WorkbookPath);
        Assert.Equal(ReaderResult.Supported, read.Result);

        using var zip = ZipFile.OpenRead(WorkbookPath);
        var parts = WorksheetParts(zip);

        var checkedSheets = 0;

        foreach (var sheet in read.Sheets.Where(s => s.Presence == SheetPresence.Bound))
        {
            var part = parts.FirstOrDefault(p =>
                string.Equals(Path.GetFileName(p), PartNameFor(zip, sheet.PhysicalName!), StringComparison.OrdinalIgnoreCase));

            if (part is null || sheet.HeaderRow is null)
            {
                continue;
            }

            var document = Load(zip, part);
            var dataRows = Rows(document, SpreadsheetMl)
                .Select(r => (int?)int.Parse(r.Attribute("r")!.Value))
                .Where(n => n > sheet.HeaderRow)
                .ToArray();

            if (dataRows.Length == 0)
            {
                continue;
            }

            checkedSheets++;

            // The container holds rows below this sheet's header. The canonical reader must not
            // report the sheet as having none.
            Assert.NotEmpty(sheet.Records);

            // ...and it must not silently drop the last of them, which is the shape a counting error
            // takes when it is off by one rather than broken outright.
            Assert.True(sheet.LastRow >= dataRows.Max(),
                $"'{sheet.LogicalName}' reported LastRow={sheet.LastRow} but its part holds rows to {dataRows.Max()}");
        }

        Assert.True(checkedSheets > 0,
            "at least one bound sheet must have rows for this test to have measured anything");
    }

    /// <summary>
    /// <b>The explicit control: a sheet known to hold rows reports them, and reports the right NUMBER.</b>
    ///
    /// <para>
    /// The count assertion is the one a broken counter fails. "Non-zero" alone would be satisfied by a
    /// reader that returned one row for a sheet holding three.
    /// </para>
    /// </summary>
    [Fact]
    public void The_known_populated_sheets_report_the_row_counts_the_fixture_wrote()
    {
        WriteFixture();

        var read = WorkbookCompatibilityReader.Read(WorkbookPath);

        // The governance registry: exactly the rows the fixture wrote, derived from the SAME binding
        // table the reader resolves against — so a header mismatch cannot make this assert a number
        // the fixture never produced.
        var governance = read.Sheet("GovernanceGates");
        Assert.NotNull(governance);
        Assert.Equal(SheetPresence.Bound, governance!.Presence);
        Assert.Equal(KnownGovernanceRows, governance.Records.Count);

        // ...and the general control. `00_Control` is filled to row 22 by the fixture builder, so it is
        // a sheet whose non-emptiness does not depend on anything this milestone added.
        var control = read.Sheet("Control");
        Assert.NotNull(control);
        Assert.NotEmpty(control!.Records);

        // The two are asserted together because either alone can pass for the wrong reason: the
        // governance count could match a fixture that wrote nothing anywhere, and the control could be
        // non-empty while governance read as zero.
        Assert.True(control.Records.Count > KnownGovernanceRows,
            "the control sheet must be the larger of the two, or this fixture is not measuring what it claims");
    }

    /// <summary>
    /// <b>The same control on the LIVE authority, when it is present.</b>
    ///
    /// <para>
    /// This is the measurement that caught the trap in the first place, and it is worth keeping where
    /// the estate exists. It is NOT the portable proof — the tests above are, and they run
    /// everywhere. Skipping here costs nothing because the assertions above have already made the
    /// claim; what this adds is the real workbook's own numbers beside it.
    /// </para>
    /// </summary>
    [Fact]
    public void The_live_authoritys_known_populated_sheets_are_non_zero()
    {
        if (!TestEstate.LiveEstateAvailable)
        {
            // Not a skipped proof: the portable controls above carry the claim. This is the real
            // estate's own numbers, recorded where the estate exists.
            return;
        }

        var path = Path.Combine(TestEstate.Root, "DevelopmentControl", "NEXUS_DEVELOPMENT_CONTROL.xlsx");
        var read = WorkbookCompatibilityReader.Read(path);

        Assert.Equal(WorkbookForm.V3, read.Form);

        using var zip = ZipFile.OpenRead(path);

        // The known-good control: the sheet every count in this programme is calibrated against.
        var workItems = read.Sheet("WorkGraph");
        Assert.NotNull(workItems);
        Assert.True(workItems!.Records.Count > 0,
            "07_WorkItems holds rows in the canonical workbook; a zero here means the reader lost them");

        // ...and the registry this milestone bound, measured rather than assumed. The number is NOT
        // asserted: W10.5B's own acceptance rule forbids hardcoding it, because a registry that gained
        // a record legitimately would then fail its own test.
        var governance = read.Sheet("GovernanceGates");
        Assert.NotNull(governance);

        // What IS asserted is the relationship the trap would break: if the sheet's part holds rows,
        // the reader must see them.
        var part = WorksheetParts(zip).Single(p => p.EndsWith("sheet19.xml", StringComparison.OrdinalIgnoreCase));
        var rows = Rows(Load(zip, part), SpreadsheetMl).Count();

        Assert.True(rows > 0, "18_Governance holds rows in the canonical workbook");
        Assert.Equal(rows - 4, governance!.Records.Count);
    }

    // ================================================================ helpers

    private static string[] WorksheetParts(ZipArchive zip) => zip.Entries
        .Select(e => e.FullName)
        .Where(n => n.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                 && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToArray();

    private static XDocument Load(ZipArchive zip, string part)
    {
        using var stream = zip.GetEntry(part)!.Open();
        return XDocument.Load(stream);
    }

    private static IEnumerable<XElement> Rows(XDocument document, XNamespace ns)
    {
        var sheetData = document.Root!.Element(ns + "sheetData");
        return sheetData is null ? [] : sheetData.Elements(ns + "row");
    }

    /// <summary>The worksheet part a physical sheet name resolves to, from the workbook's own rels.</summary>
    private static string? PartNameFor(ZipArchive zip, string physicalName)
    {
        var workbook = Load(zip, "xl/workbook.xml");
        var rels = Load(zip, "xl/_rels/workbook.xml.rels");
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var targets = rels.Root!
            .Elements()
            .ToDictionary(e => e.Attribute("Id")!.Value, e => e.Attribute("Target")!.Value);

        var sheet = workbook.Root!
            .Element(SpreadsheetMl + "sheets")!
            .Elements(SpreadsheetMl + "sheet")
            .FirstOrDefault(s => string.Equals(s.Attribute("name")?.Value, physicalName, StringComparison.Ordinal));

        if (sheet is null)
        {
            return null;
        }

        var target = targets[sheet.Attribute(r + "id")!.Value];
        return Path.GetFileName(target);
    }
}
