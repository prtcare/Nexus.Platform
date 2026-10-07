using System.IO.Compression;
using System.Text;
using Nexus.DevelopmentControl.Safety;
using Nexus.ProductCore.Contracts.DevelopmentControl;

namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// W10.0A FINAL TASK 4 — <b>deterministic DevelopmentControl workbooks, built from the schema
/// declaration rather than from a copy of the estate's files.</b>
///
/// <para>
/// <b>Why this exists.</b> The DevelopmentControl suites read their subject workbook from a real file
/// on the estate host. That made the repository's own test suite depend on one developer's machine:
/// on the Linux CI runner 81 tests failed with <c>FileNotFoundException</c>, and the failures said
/// nothing about the component — only about where the tests were run.
/// </para>
///
/// <para>
/// <b>Why a builder and not a committed <c>.xlsx</c>.</b> The real workbooks carry live estate
/// records. Committing one would move estate data into a <b>public</b> repository to satisfy a test,
/// which is a far worse trade than the portability it buys. The tests do not need those records; they
/// need a file with the right <i>structure</i>.
/// </para>
///
/// <para>
/// <b>Why the structure is DERIVED and not written out here.</b> The header rows come from
/// <see cref="WorkbookCompatibilityMap"/> — the same table the reader resolves logical columns against
/// — and the marker cells come from <see cref="DevelopmentControlAuthoritySites.All"/> — the same list
/// the writer guards and the cutover writes. A fixture with a hand-copied header list would be a
/// second declaration of the schema, and the failure mode of two declarations is not a build error:
/// it is a suite that keeps passing after the schema changes and a component that has quietly stopped
/// being tested. Deriving them means a schema change moves the fixture with it.
/// </para>
///
/// <para>
/// <b>Three silent traps this builder exists to avoid</b> — each produces a confusing failure rather
/// than an error:
/// </para>
/// <list type="number">
/// <item><description>A <c>&lt;row&gt;</c> without an integer <c>r</c> is <b>silently dropped</b> by
/// the reader. The header row would vanish and every required column would read as missing, so the
/// workbook looks like a schema problem. Every row here carries <c>r</c>.</description></item>
/// <item><description>Relationship <c>Target</c> must be <b>package-absolute</b> (leading <c>/</c>).
/// The reader normalises a bare path; the writer does not, and throws
/// <c>The container has no part …</c> — which the append path surfaces as a refusal that reads like a
/// schema rejection.</description></item>
/// <item><description>Two rows declaring the same <c>ControlItem</c> cause the marker key to be
/// <b>removed</b>, never overwritten, and the authority marker then reads as absent — so
/// <c>00_Control</c> is built with exactly one row per item name.</description></item>
/// </list>
/// </summary>
internal static class WorkbookFixtureBuilder
{
    private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>The schema identity the write gate compares — byte for byte, case-sensitively.</summary>
    private const string SchemaId = "NEXUS-DEVELOPMENTCONTROL";
    private const string SchemaVersion = "V3.0";

    private const int HeaderRow = 4;

    // ================================================================ public entry points

    /// <summary>
    /// A V3 workbook whose four authority sites read <c>CANDIDATE</c> — the state the model carries
    /// before the cutover act, and the only state the cutover's positive path can run against, because
    /// promoting an already-authoritative workbook is correctly refused as a no-op.
    /// </summary>
    public static string Candidate(string path) => BuildV3(path, DevelopmentControlAuthoritySites.Candidate);

    /// <summary>A V3 workbook whose four authority sites read <c>AUTHORITATIVE</c>.</summary>
    public static string Authoritative(string path) => BuildV3(path, DevelopmentControlAuthoritySites.Authoritative);

    /// <summary>
    /// A V3 workbook carrying a populated <c>18_Governance</c> — the Governance Gate Registry.
    ///
    /// <para>
    /// <b>A builder variant rather than a committed workbook</b>, for the reason the whole builder
    /// exists: the real registry holds 91 live governance records and committing one would move estate
    /// data into a public repository to satisfy a test. What these tests need is the <i>shape</i> —
    /// rows with identities, planning statuses and blanks in the join-candidate columns.
    /// </para>
    ///
    /// <para>
    /// The three flags exist because the failure controls this milestone must prove are not variations
    /// of a valid workbook: each produces a workbook that is wrong in a different way, and a fixture
    /// that could only build the good case would let every refusal assertion pass vacuously.
    /// </para>
    /// </summary>
    public static string AuthoritativeWithGovernance(
        string path,
        IReadOnlyList<GovernanceRow> rows,
        bool omitGovernanceSheet = false,
        bool renameGovernanceIdentityColumn = false) =>
        BuildV3(path, DevelopmentControlAuthoritySites.Authoritative, rows, omitGovernanceSheet,
            renameGovernanceIdentityColumn);

    /// <summary>
    /// One registry row, given by HEADER NAME rather than by column letter.
    ///
    /// <para>
    /// Taking the fields by name is deliberate: a fixture that addressed <c>A5</c> would keep passing
    /// after the binding moved <c>GovernanceId</c>, and would be measuring the fixture's own column
    /// arithmetic rather than the reader's resolution. Every value here is written into the column the
    /// READER will resolve, because the fixture derives its headers from the same binding table.
    /// </para>
    /// </summary>
    public sealed record GovernanceRow(
        string GovernanceId,
        string RegistryStatus = "",
        string Name = "",
        string GateId = "");

    /// <summary>
    /// A workbook in the legacy form. <b>The form is identified by sheet NAMES alone</b> —
    /// <c>Control Center</c> and <c>Master Roadmap</c> — and never by a sheet count, so the legacy
    /// fixture carries exactly those two and an append against it is refused with a schema mismatch.
    /// </summary>
    public static string Legacy(string path)
    {
        var sheets = new List<Sheet>
        {
            new("Control Center", new[] { "Item", "Value" }, new SortedDictionary<int, string>()),
            new("Master Roadmap", new[] { "Id", "State" }, new SortedDictionary<int, string>())
        };

        return Write(path, sheets);
    }

    // ================================================================ the V3 template

    private sealed record Sheet(string Name, IReadOnlyList<string> Headers, SortedDictionary<int, string> Cells);

    private static string BuildV3(
        string path,
        string state,
        IReadOnlyList<GovernanceRow>? governance = null,
        bool omitGovernanceSheet = false,
        bool renameGovernanceIdentityColumn = false)
    {
        var sheets = new List<Sheet>();
        var covered = new HashSet<string>(StringComparer.Ordinal);

        // ---- every sheet the V3 form binds, with its headers derived from the map ---------------
        foreach (var binding in WorkbookCompatibilityMap.Sheets)
        {
            if (binding.V3Name is null)
            {
                continue;
            }

            var physical = binding.V3Name;
            covered.Add(physical);

            // W10.5B. The one variant that removes a bound sheet from the container entirely, so the
            // reader reports DECLARED_BUT_MISSING rather than an empty registry. Deleting the column
            // from `covered` as well keeps the sheet list honest: it is genuinely not in the package.
            if (omitGovernanceSheet && binding.LogicalName == "GovernanceGates")
            {
                covered.Remove(physical);
                continue;
            }

            var headers = HeadersFor(binding.LogicalName);
            var cells = new SortedDictionary<int, string>();

            if (binding.LogicalName == "GovernanceGates")
            {
                // The identity column is renamed rather than deleted, and the distinction is the
                // point: a DELETED column would leave the header row one shorter and could be read as
                // a different sheet. A renamed one occupies the same position and holds the same kind
                // of value, so a positional reader would find it and never notice — which is exactly
                // the "plausible value from the adjacent column" failure that resolving by name
                // exists to prevent, and exactly what the required-column rule must refuse.
                //
                // The rows are still populated, under the DRIFTED spelling. A workbook whose header
                // moved is one that already holds data; leaving the sheet empty would make the test
                // prove only that an empty sheet with a strange header is refused.
                const string Renamed = "GovernanceIdentifier";
                var identityHeader = "GovernanceId";

                if (renameGovernanceIdentityColumn)
                {
                    var at = headers.FindIndex(h => string.Equals(h, "GovernanceId", StringComparison.Ordinal));
                    if (at >= 0)
                    {
                        headers[at] = Renamed;
                        identityHeader = Renamed;
                    }
                }

                WriteGovernanceCells(cells, headers, governance ?? [], identityHeader);
            }

            if (binding.LogicalName == "Control")
            {
                // The schema declaration rows the write gate compares by NAME, plus the marker row.
                // The marker row must be row 10 because site B10 is a fixed address.
                Set(cells, "A5", "SchemaId"); Set(cells, "B5", SchemaId);
                Set(cells, "A6", "SchemaVersion"); Set(cells, "B6", SchemaVersion);
                Set(cells, "A10", "ControlState"); Set(cells, "B10", state);

                // Fill to row 22, because the sheet's LAST ROW is load-bearing rather than cosmetic:
                // a control that plants a row to test what a shadowed marker resolves to plants it at
                // row 22, which must be one past this sheet's end for the shadowing position to be the
                // one under test. A shorter sheet would put the planted row INSIDE the existing range
                // and the test would be measuring a different thing while still compiling.
                //
                // Every filler item name is distinct: `ControlItems` REMOVES a key on a duplicate
                // rather than overwriting it, so a repeated name would silently drop that item from the
                // projection — and `ControlState` in particular must survive.
                for (var row = 5; row <= 22; row++)
                {
                    if (row is 5 or 6 or 10)
                    {
                        continue;
                    }

                    // Row 11 carries a REAL item name rather than a filler, because a probe uses it as
                    // "an ordinary row with an ordinary item" and asserts that item by name before
                    // trying to write the reserved marker name into it. A filler name there would make
                    // the probe fail on its own premise — and the premise is what distinguishes "the
                    // reserved-name rule held" from "the write never reached the rule".
                    Set(cells, $"A{row}", row == 11 ? "MigrationVersion" : $"FixtureItem{row:00}");
                    Set(cells, $"B{row}", $"fixture-value-{row:00}");
                }
            }

            if (binding.LogicalName == "MigrationMap")
            {
                // The migration row whose STATUS the cutover flips. Its identity cell (A16) is not
                // itself an authority site — which is the whole premise of the probe that writes to it
                // and asserts the marker did not move. The spelling is lower-case because the
                // migration it names is `unified-control`.
                Set(cells, "A16", "MAP-unified-control");
            }

            sheets.Add(new Sheet(physical, headers, cells));
        }

        // ---- the two sheets that carry an authority site but that the map does not bind ----------
        // Their logical sheet is null by declaration: an ordinary logical cell write cannot reach
        // these sites at all, so they need no header row — only the cell the cutover addresses.
        foreach (var name in new[] { "01_Configuration", "25_Dashboard" })
        {
            if (covered.Add(name))
            {
                sheets.Add(new Sheet(name, Array.Empty<string>(), new SortedDictionary<int, string>()));
            }
        }

        // ---- place every marker, from the declaration the writer and cutover also read ----------
        foreach (var site in DevelopmentControlAuthoritySites.All)
        {
            var sheet = sheets.FirstOrDefault(s => string.Equals(s.Name, site.PhysicalSheet, StringComparison.Ordinal));

            if (sheet is null)
            {
                throw new InvalidOperationException(
                    $"Authority site '{site.Reference}' names a sheet this fixture does not build. The site "
                    + "list and the sheet list have drifted, and a fixture missing a marker site would "
                    + "produce a workbook whose authority reads as absent rather than as wrong.");
            }

            // When the site is reachable by a logical write, its COLUMN must carry that logical
            // column's header — otherwise the projection would resolve the name to one column while
            // the cutover writes another, and the two would disagree silently.
            var headers = sheet.Headers.ToList();
            if (site is { LogicalColumn: not null, LogicalSheet: not null })
            {
                EnsureHeaderAt(headers, site.LogicalColumn, ColumnIndexOf(site.PhysicalColumn));
            }

            var cells = new SortedDictionary<int, string>(sheet.Cells);
            Set(cells, site.PhysicalColumn + site.Row, state);

            sheets[sheets.IndexOf(sheet)] = sheet with { Headers = headers, Cells = cells };
        }

        return Write(path, sheets);
    }

    /// <summary>
    /// W10.5B. Lays the Governance rows under the header list the binding produced, addressing each
    /// value by its HEADER NAME rather than by a column letter.
    ///
    /// <para>
    /// <b>A blank <c>GovernanceId</c> still emits its row</b>, provided some other field carries a
    /// value. That is not a convenience: <c>ReadSheet</c> drops a row whose cells are ALL empty, so a
    /// row left entirely blank would never reach the projection and the blank-identity refusal would
    /// be tested by a fixture that produced no record at all. Giving the row a Name is what makes the
    /// record exist and the identity blank — which is the malformed record under test.
    /// </para>
    /// </summary>
    private static void WriteGovernanceCells(
        SortedDictionary<int, string> cells, List<string> headers, IReadOnlyList<GovernanceRow> rows,
        string identityHeader)
    {
        // Addressed by LOGICAL name and resolved to the header text the binding declares, never by the
        // sheet's own spelling. That indirection is load-bearing here rather than tidy: `RegistryStatus`
        // is bound to the physical header `Status`, so a fixture that looked for the literal it was
        // handed would find nothing — and the failure it would produce is a fixture that writes no rows
        // at all, which every assertion below would then satisfy vacuously.
        //
        // `GovernanceId` is the one exception, and deliberately so: in the drifted-header variant the
        // identity column's spelling IS the thing under test, so it is supplied rather than resolved.
        var headerFor = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GovernanceId"] = identityHeader,
            ["RegistryStatus"] = Physical("RegistryStatus"),
            ["Name"] = Physical("Name"),
            ["GateId"] = Physical("GateId"),
        };

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = HeaderRow + 1 + i;

            Write("GovernanceId", row.GovernanceId);
            Write("RegistryStatus", row.RegistryStatus);
            Write("Name", row.Name);
            Write("GateId", row.GateId);

            void Write(string logical, string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return;
                }

                var at = headers.FindIndex(h =>
                    string.Equals(h, headerFor[logical], StringComparison.Ordinal));

                if (at < 0)
                {
                    throw new InvalidOperationException(
                        $"the Governance fixture cannot write '{logical}' (header '{headerFor[logical]}'): "
                        + "the binding did not emit that header. The fixture addresses columns through the "
                        + "binding precisely so this fails here rather than producing a workbook whose rows "
                        + "land in the wrong places.");
                }

                Set(cells, ColumnLetter(at + 1) + rowNumber, value);
            }
        }

        static string Physical(string logical)
        {
            var declared = WorkbookCompatibilityMap.ColumnsFor("GovernanceGates")
                .FirstOrDefault(c => string.Equals(c.LogicalName, logical, StringComparison.Ordinal));

            return declared?.V3 ?? throw new InvalidOperationException(
                $"the Governance fixture addresses logical column '{logical}', which the binding does not "
                + "declare for 'GovernanceGates'.");
        }
    }

    /// <summary>
    /// The header row for a logical sheet, in an order that preserves the declaration's occurrence
    /// sequencing.
    ///
    /// <para>
    /// <b>The duplicated <c>ChangeId</c> is the load-bearing part.</b> Five V3 sheets carry that header
    /// twice: the first occurrence binds the business key, the second the envelope's originating
    /// change. Occurrences are ordered by physical column index, so emitting one column per binding in
    /// declaration order is what puts them the right way round. With only one, the envelope stays
    /// unresolved and every append is refused for a missing envelope column — a failure that looks
    /// like a component defect and is a fixture defect.
    /// </para>
    /// </summary>
    private static List<string> HeadersFor(string logicalSheet)
    {
        var headers = new List<string>();

        // One column per binding, in declaration order. A binding with a null V3 header is
        // absent-in-form and contributes no column.
        foreach (var column in WorkbookCompatibilityMap.ColumnsFor(logicalSheet))
        {
            if (column.V3 is not null)
            {
                headers.Add(column.V3);
            }
        }

        // The envelope is PROJECTED onto every sheet. For each envelope member, ask whether this
        // sheet already binds it explicitly; if it does not, the projection takes the first
        // occurrence of the header text that no binding claimed — which means emitting one MORE.
        //
        // The distinction is the whole reason `Changes` failed before this rule: it binds
        // `ChangeId` (occurrence 1, the business key) and does NOT bind `EnvelopeChangeId`. So its
        // envelope needs a SECOND `ChangeId` column, and "the text is already present, skip it"
        // silently left the envelope unresolved — surfacing as an append refusal for a missing
        // envelope column, which reads as a component defect.
        var bound = WorkbookCompatibilityMap.ColumnsFor(logicalSheet);
        foreach (var (envelopeName, headerText) in WorkbookCompatibilityMap.EnvelopeProjection)
        {
            if (bound.Any(c => c.LogicalName == envelopeName && c.V3 == headerText))
            {
                continue;
            }

            headers.Add(headerText);
        }

        return headers;
    }

    /// <summary>Moves a header to a required column index, swapping with whatever is there.</summary>
    private static void EnsureHeaderAt(List<string> headers, string header, int oneBasedColumn)
    {
        var index = oneBasedColumn - 1;

        while (headers.Count <= index)
        {
            headers.Add(string.Empty);
        }

        var current = headers.FindIndex(h => string.Equals(h, header, StringComparison.Ordinal));
        if (current == index)
        {
            return;
        }

        if (current >= 0)
        {
            (headers[current], headers[index]) = (headers[index], headers[current]);
        }
        else
        {
            headers[index] = header;
        }
    }

    private static void Set(SortedDictionary<int, string> cells, string address, string value)
    {
        var column = ColumnIndexOf(address.LeadingLetters());
        var row = RowOf(address);
        cells[RowColumnKey(row, column)] = value;
    }

    // Cells are keyed by (row, column) packed into one int so a SortedDictionary orders them
    // row-major — the order the writer inserts in.
    private static int RowColumnKey(int row, int column) => (row * 1000) + column;

    // ================================================================ OOXML emission

    private static string Write(string path, List<Sheet> sheets)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        AddEntry(zip, "[Content_Types].xml", ContentTypes(sheets.Count));
        AddEntry(zip, "xl/workbook.xml", WorkbookXml(sheets));
        AddEntry(zip, "xl/_rels/workbook.xml.rels", RelationshipsXml(sheets.Count));

        for (var i = 0; i < sheets.Count; i++)
        {
            AddEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", SheetXml(sheets[i]));
        }

        return path;
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);

        // A fixed timestamp, so two builds of the same fixture are byte-identical and a hash
        // comparison taken across a write means something.
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string ContentTypes(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");

        for (var i = 1; i <= sheetCount; i++)
        {
            sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string WorkbookXml(List<Sheet> sheets)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append($"<workbook xmlns=\"{NsMain}\" xmlns:r=\"{NsRel}\"><sheets>");

        for (var i = 0; i < sheets.Count; i++)
        {
            sb.Append($"<sheet name=\"{Escape(sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        }

        sb.Append("</sheets></workbook>");
        return sb.ToString();
    }

    private static string RelationshipsXml(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append($"<Relationships xmlns=\"{NsPkgRel}\">");

        for (var i = 1; i <= sheetCount; i++)
        {
            // PACKAGE-ABSOLUTE. Trap 2: the writer does not normalise a bare target, and reports the
            // missing part as an append refusal.
            sb.Append($"<Relationship Id=\"rId{i}\" Type=\"{NsRel}/worksheet\" Target=\"/xl/worksheets/sheet{i}.xml\"/>");
        }

        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string SheetXml(Sheet sheet)
    {
        // (row, column) -> value, seeded from the header row and overlaid with the declared cells.
        var values = new SortedDictionary<int, string>();

        for (var i = 0; i < sheet.Headers.Count; i++)
        {
            if (!string.IsNullOrEmpty(sheet.Headers[i]))
            {
                values[RowColumnKey(HeaderRow, i + 1)] = sheet.Headers[i];
            }
        }

        foreach (var (key, value) in sheet.Cells)
        {
            values[key] = value;
        }

        if (values.Count == 0)
        {
            // An unbound site sheet with no declared cells would emit an empty sheetData, which the
            // WRITER refuses ("no rows"). Give it a row so it is a legal container either way.
            values[RowColumnKey(1, 1)] = sheet.Name;
        }

        var rows = values
            .GroupBy(kv => kv.Key / 1000)
            .OrderBy(g => g.Key)
            .Select(g => (Row: g.Key, Cells: g.OrderBy(c => c.Key % 1000).ToList()))
            .ToList();

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append($"<worksheet xmlns=\"{NsMain}\">");

        var lastColumn = ColumnLetter(values.Keys.Select(k => k % 1000).Max());
        sb.Append($"<dimension ref=\"A{rows[0].Row}:{lastColumn}{rows[^1].Row}\"/>");
        sb.Append("<sheetData>");

        foreach (var (row, cells) in rows)
        {
            // `r` is REQUIRED — trap 1. A row without it is dropped without comment.
            sb.Append($"<row r=\"{row}\">");

            foreach (var (key, value) in cells)
            {
                var address = ColumnLetter(key % 1000) + row;
                sb.Append($"<c r=\"{address}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(value)}</t></is></c>");
            }

            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    // ================================================================ address arithmetic

    /// <summary>1 → A, 26 → Z, 27 → AA. Matches the column letters the workbook format uses.</summary>
    private static string ColumnLetter(int index)
    {
        var sb = new StringBuilder();
        while (index > 0)
        {
            var remainder = (index - 1) % 26;
            sb.Insert(0, (char)('A' + remainder));
            index = (index - 1) / 26;
        }

        return sb.ToString();
    }

    /// <summary>"R" → 18, "AA" → 27.</summary>
    private static int ColumnIndexOf(string letters)
    {
        var index = 0;
        foreach (var c in letters)
        {
            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return index;
    }

    private static string LeadingLetters(this string address)
    {
        var letters = 0;
        while (letters < address.Length && char.IsLetter(address[letters]))
        {
            letters++;
        }

        return address[..letters];
    }

    private static int RowOf(this string address)
    {
        var letters = address.LeadingLetters().Length;
        return int.Parse(address[letters..]);
    }
}
