using System.IO.Compression;
using System.Xml.Linq;
using Nexus.DevelopmentControl.Safety;

namespace Nexus.ProductCore.Core.DevelopmentControl;

/// <summary>
/// W8D-R4 TASK 9. The canonical cell write mechanism: writes ONE cell into a named sheet of a
/// DevelopmentControl workbook, atomically, changing nothing else.
///
/// <para><b>Why this is the minimum, and why that is deliberate.</b> TASK 9 asks for "read,
/// reserve, write, read-back" through the shared component. It does not ask for a general
/// workbook editor, and building one would be the wrong shape for this component: every extra
/// capability here is a new way to corrupt the authoritative governance record. So this writes a
/// single named cell into a single named sheet and copies every other part of the container
/// through untouched.</para>
///
/// <para><b>Inline strings, never the shared string table.</b> This is the central design
/// decision. The V3 candidate carries <c>xl/sharedStrings.xml</c> with 6109 entries, and every
/// string cell in it is an index into that table (<c>t="s"</c>). A writer that appended to it
/// would have to renumber nothing but would mutate a part shared by all 26 sheets — one indexing
/// mistake there silently corrupts every string in the workbook, and the damage is invisible to a
/// spot check because the indices still resolve to <i>some</i> string. Writing
/// <c>t="inlineStr"</c> instead leaves the shared table byte-identical and localises the change to
/// one cell. The reader already decodes both forms (WC-3 in WorkbookCompatibilityReader), so this
/// costs nothing at read time.</para>
///
/// <para><b>Atomicity.</b> The new container is built in full beside the target and then swapped
/// in with <see cref="File.Replace(string,string,string,bool)"/>, which is atomic on NTFS. A crash
/// mid-write therefore leaves either the original workbook or the new one, never a truncated file.
/// Writing in place would leave a half-written governance record, which is worse than no write.</para>
///
/// <para><b>What this does NOT do.</b> It does not validate governance, does not acquire the lock,
/// and does not decide whether a write is permitted. Callers must hold the writer lock and have a
/// permitted authorisation; those are separate mechanisms and conflating them here would make the
/// lock bypassable by anyone who can construct a writer.</para>
/// </summary>
internal static class DevelopmentControlCellWriter
{
    private static readonly XNamespace Ns =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace Rns =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace PrNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// Writes <paramref name="value"/> into the cell at
    /// (<paramref name="physicalSheetName"/>, <paramref name="rowNumber"/>,
    /// <paramref name="columnLetter"/>) and swaps the result in atomically.
    ///
    /// <para>Throws <see cref="WorkbookDecodeException"/> when the sheet or the row cannot be
    /// located. It does NOT create a missing row or sheet: a write that silently appends to a
    /// workbook whose structure has changed is exactly the "wrote something plausible to the wrong
    /// place" failure this component exists to prevent.</para>
    /// </summary>
    internal static void WriteCell(
        string workbookPath, string physicalSheetName, int rowNumber, string columnLetter, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalSheetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnLetter);
        ArgumentNullException.ThrowIfNull(value);

        var partName = ResolveSheetPart(workbookPath, physicalSheetName);
        var columnIndex = ColumnIndex(columnLetter);

        var directory = Path.GetDirectoryName(Path.GetFullPath(workbookPath))
            ?? throw new WorkbookDecodeException($"'{workbookPath}' has no containing directory.");

        var temp = Path.Combine(directory,
            Path.GetFileName(workbookPath) + ".w8dr4-" + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        var backup = temp + ".bak";

        try
        {
            RewriteContainer(workbookPath, temp, partName, rowNumber, columnIndex, columnLetter, value);

            // Atomic swap. File.Replace is the only one of the .NET file APIs that gives
            // all-or-nothing semantics on NTFS with a rollback copy; Move(overwrite) is close but
            // gives no recovery file, and Copy is not atomic at all.
            File.Replace(temp, Path.GetFullPath(workbookPath), backup, ignoreMetadataErrors: true);
        }
        finally
        {
            foreach (var stray in new[] { temp, backup })
            {
                try { if (File.Exists(stray)) File.Delete(stray); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// Builds the new container: every part copied through byte-for-byte except the target sheet,
    /// which is re-serialised with the one cell changed.
    /// </summary>
    private static void RewriteContainer(
        string source, string destination, string partName,
        int rowNumber, int columnIndex, string columnLetter, string value)
    {
        using var input = ZipFile.OpenRead(source);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);

        foreach (var entry in input.Entries)
        {
            // Directories carry no content; a zero-length entry with a trailing slash is a
            // directory in the OPC model and copying it as a file would produce an invalid package.
            if (entry.FullName.EndsWith('/')) continue;

            var created = archive.CreateEntry(entry.FullName, CompressionLevel.Optimal);

            // Determinism. ZipArchive.CreateEntry stamps every entry with DateTime.Now by default,
            // so two writes of the same content at different instants produce DIFFERENT bytes for
            // the same logical result. For a governance artifact that matters: it defeats
            // content-addressed verification, makes "did this workbook change?" unanswerable by
            // hash alone, and turns any byte-comparison proof into a clock comparison.
            //
            // Measured, not theorised: the cross-host write proof compared Developer's copy with
            // Forge's copy after writing the same cell through the same component and got two
            // different SHA-256s. Carrying the SOURCE entry's timestamp through makes the output a
            // function of the input alone.
            created.LastWriteTime = entry.LastWriteTime;

            using var target = created.Open();

            if (string.Equals(entry.FullName, partName, StringComparison.OrdinalIgnoreCase))
            {
                var edited = EditSheet(entry, rowNumber, columnIndex, columnLetter, value);
                target.Write(edited);
            }
            else
            {
                using var original = entry.Open();
                original.CopyTo(target);
            }
        }
    }

    /// <summary>
    /// Loads the sheet part, sets the one cell, and returns the re-serialised bytes.
    ///
    /// <para>The cell is replaced in place when it already exists, and inserted at its column
    /// position when it does not — OOXML requires cells within a row to be in ascending column
    /// order, and a row whose cells are out of order is rejected by stricter consumers even though
    /// this reader would still resolve it by name.</para>
    /// </summary>
    private static byte[] EditSheet(
        ZipArchiveEntry entry, int rowNumber, int columnIndex, string columnLetter, string value)
    {
        XDocument doc;
        using (var stream = entry.Open())
        {
            doc = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }

        var root = doc.Root
            ?? throw new WorkbookDecodeException($"Sheet part '{entry.FullName}' has no root element.");

        var sheetData = root.Element(Ns + "sheetData")
            ?? throw new WorkbookDecodeException($"Sheet part '{entry.FullName}' has no sheetData element.");

        var row = sheetData.Elements(Ns + "row")
            .FirstOrDefault(r => (string?)r.Attribute("r") == rowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ?? throw new WorkbookDecodeException(
                $"Row {rowNumber} does not exist in sheet part '{entry.FullName}'. This writer does not "
                + "create rows: a write that appends to a structure which has changed underneath it is "
                + "the failure mode this component exists to prevent. Re-read the workbook and target "
                + "a row it actually carries.");

        var reference = columnLetter + rowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var cell = row.Elements(Ns + "c")
            .FirstOrDefault(c => string.Equals((string?)c.Attribute("r"), reference, StringComparison.OrdinalIgnoreCase));

        var newCell = new XElement(Ns + "c",
            new XAttribute("r", reference),
            new XAttribute("t", "inlineStr"),
            new XElement(Ns + "is", new XElement(Ns + "t", value)));

        if (cell is not null)
        {
            // Preserve the style index: dropping it would silently restyle the cell, which is a
            // visible change to the workbook that has nothing to do with the value being written.
            var style = cell.Attribute("s");
            if (style is not null) newCell.SetAttributeValue("s", style.Value);
            cell.ReplaceWith(newCell);
        }
        else
        {
            var inserted = false;
            foreach (var existing in row.Elements(Ns + "c"))
            {
                var existingRef = (string?)existing.Attribute("r");
                if (existingRef is null) continue;
                if (ColumnIndex(ColumnLetters(existingRef)) > columnIndex)
                {
                    existing.AddBeforeSelf(newCell);
                    inserted = true;
                    break;
                }
            }

            if (!inserted) row.Add(newCell);
        }

        using var buffer = new MemoryStream();
        doc.Save(buffer, SaveOptions.DisableFormatting);
        return buffer.ToArray();
    }

    /// <summary>
    /// Resolves a physical sheet name to its part path by reading <c>xl/workbook.xml</c> and the
    /// relationship map, exactly as the reader does. A sheet the workbook does not declare is an
    /// error, never a guess at a conventional part name.
    /// </summary>
    private static string ResolveSheetPart(string workbookPath, string physicalSheetName)
    {
        using var zip = ZipFile.OpenRead(workbookPath);

        var workbook = LoadPart(zip, "xl/workbook.xml");
        var rels = LoadPart(zip, "xl/_rels/workbook.xml.rels");

        var targets = rels.Root!
            .Elements(PrNs + "Relationship")
            .ToDictionary(
                r => (string)r.Attribute("Id")!,
                r => (string)r.Attribute("Target")!,
                StringComparer.Ordinal);

        foreach (var sheet in workbook.Root!.Element(Ns + "sheets")!.Elements(Ns + "sheet"))
        {
            if (!string.Equals((string?)sheet.Attribute("name"), physicalSheetName, StringComparison.Ordinal))
                continue;

            var id = (string?)sheet.Attribute(Rns + "id")
                ?? throw new WorkbookDecodeException($"Sheet '{physicalSheetName}' declares no relationship id.");

            if (!targets.TryGetValue(id, out var target))
                throw new WorkbookDecodeException($"Sheet '{physicalSheetName}' references missing relationship '{id}'.");

            // Relationship targets are package-absolute here ("/xl/worksheets/sheetN.xml"), which is
            // why the reader's own resolution and this one both strip the leading slash rather than
            // concatenating a base path. Measured against the V3 candidate, not assumed.
            return target.TrimStart('/');
        }

        throw new WorkbookDecodeException(
            $"The workbook declares no sheet named '{physicalSheetName}'. Available sheets: "
            + string.Join(", ", workbook.Root!.Element(Ns + "sheets")!.Elements(Ns + "sheet")
                .Select(s => (string?)s.Attribute("name"))));
    }

    private static XDocument LoadPart(ZipArchive zip, string partName)
    {
        var entry = zip.GetEntry(partName)
            ?? throw new WorkbookDecodeException($"The container has no '{partName}' part.");

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    /// <summary>"AB" → 28 (1-based). OOXML column references are bijective base-26.</summary>
    internal static int ColumnIndex(string letters)
    {
        var index = 0;
        foreach (var ch in letters)
        {
            if (ch is >= 'A' and <= 'Z') index = index * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') index = index * 26 + (ch - 'a' + 1);
            else break;
        }

        return index;
    }

    /// <summary>"AB12" → "AB".</summary>
    internal static string ColumnLetters(string cellReference)
    {
        var end = 0;
        while (end < cellReference.Length && char.IsLetter(cellReference[end])) end++;
        return cellReference[..end];
    }
}
