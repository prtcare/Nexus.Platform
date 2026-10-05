using System.IO.Compression;
using System.Xml.Linq;
using Nexus.DevelopmentControl.Safety;
using Nexus.ProductCore.Contracts.DevelopmentControl;

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
    ///
    /// <para><b>W8D FINAL: it also refuses the authority-marker sites.</b> Until this change an
    /// ordinary cell write to <c>00_Control!B10</c> promoted the workbook to authoritative and the
    /// reader accepted the result, so "a normal workbook write must NOT promote authority" was
    /// false — not weakly enforced, but untrue. The check is PHYSICAL and lives here, at the last
    /// point before a cell is touched, because a guard phrased in logical names would sit one layer
    /// up and any future caller resolving a physical column another way would step around it.
    /// Promotion goes through <see cref="CutoverAuthority"/>, which is the only caller permitted to
    /// write these cells and the only one that does not come through this method.</para>
    /// </summary>
    internal static void WriteCell(
        string workbookPath, string physicalSheetName, int rowNumber, string columnLetter, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalSheetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnLetter);
        ArgumentNullException.ThrowIfNull(value);

        var site = DevelopmentControlAuthoritySites.FindPhysical(physicalSheetName, columnLetter, rowNumber);
        if (site is not null)
            throw new WorkbookDecodeException(DevelopmentControlAuthoritySites.RefusalReason(site));

        // --- W8D PRODUCTION WIRING: the site's IDENTITY cell, refused for the same reason and one
        // step less obvious.
        //
        // The marker is not read from a fixed address: `ControlItems` finds it by the row whose
        // `ControlItem` column reads `ControlState`. So `B10` is the cell that decides, but `A10` is
        // what makes it findable — and until this check existed `A10` was writable by any caller
        // holding the lock. Measured: an ordinary governed write of `ControlStateRenamed` there was
        // ALLOWED, after which the workbook declared no `ControlState` item at all and reported its
        // own authority as unreadable. That is degradation rather than promotion, and it is still an
        // ordinary write moving what the marker resolves to, which is what the guard above forbids.
        //
        // PHYSICAL and here, for the reason already stated above: a guard phrased in logical names
        // sits one layer up, and any caller resolving a physical column another way steps around it.
        var identity = DevelopmentControlAuthoritySites.FindPhysicalIdentity(
            physicalSheetName, columnLetter, rowNumber);
        if (identity is not null)
            throw new WorkbookDecodeException(DevelopmentControlAuthoritySites.IdentityRefusalReason(identity));

        var partName = ResolveSheetPart(workbookPath, physicalSheetName);
        var columnIndex = ColumnIndex(columnLetter);

        RewriteAtomically(workbookPath, partName,
            entry => EditSheet(entry, rowNumber, columnIndex, columnLetter, value));
    }

    /// <summary>
    /// W8D FINAL TASK 3. The explicit cutover mechanism — the ONLY code path that may write an
    /// authority-marker site.
    ///
    /// <para><b>Every site moves or none does.</b> The four sites live on four sheets, so this
    /// builds the replacement container with all of them edited and swaps it in with a single
    /// <see cref="File.Replace(string,string,string,bool)"/>. Writing them one at a time would leave
    /// a window in which the workbook reads <c>AUTHORITATIVE</c> through the resolver and
    /// <c>CANDIDATE</c> on its own dashboard — authoritative and self-contradicting at once, which
    /// is worse than either endpoint.</para>
    ///
    /// <para><b>Every site is verified before any is written.</b> The whole new container is built
    /// in memory from a read of every part, each site's current value is compared against
    /// <paramref name="fromState"/>, and a mismatch aborts before the swap. A cutover that wrote
    /// three sites and then discovered the fourth held something unexpected would have to either
    /// roll back a file it had already replaced or proceed from a state it did not understand.</para>
    ///
    /// <para>Throws <see cref="WorkbookDecodeException"/> naming the offending site. The caller
    /// (<see cref="DevelopmentControlReservation.Cutover"/>) converts that into a typed refusal.</para>
    /// </summary>
    internal static IReadOnlyList<DevelopmentControlCutoverSiteChange> CutoverAuthority(
        string workbookPath, string fromState, string toState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromState);
        ArgumentException.ThrowIfNullOrWhiteSpace(toState);

        // Resolve every site to its part first, so a missing sheet fails before anything is built.
        var planned = new List<(DevelopmentControlAuthoritySites.Site Site, string Part)>();
        foreach (var site in DevelopmentControlAuthoritySites.All)
        {
            string part;
            try { part = ResolveSheetPart(workbookPath, site.PhysicalSheet); }
            catch (WorkbookDecodeException ex)
            {
                throw new WorkbookDecodeException(
                    $"CUTOVER_SITE_MISSING — the {site.Role} authority site '{site.Reference}' is "
                    + $"declared by the model but could not be resolved: {ex.Message} A cutover moves "
                    + "every site or none; promoting a workbook that has lost one of its own markers "
                    + "would leave the remaining ones disagreeing.");
            }
            planned.Add((site, part));
        }

        var changes = new List<DevelopmentControlCutoverSiteChange>();
        var sharedStrings = ReadSharedStrings(workbookPath);
        var edits = new Dictionary<string, Func<ZipArchiveEntry, byte[]>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in planned.GroupBy(p => p.Part, StringComparer.OrdinalIgnoreCase))
        {
            var part = group.Key;
            var sitesInPart = group.ToArray();

            // Read ONCE per part, apply every site in it, serialise once. Two sites on one sheet
            // would otherwise be two loads and two saves of the same part, and the second would
            // overwrite the first.
            edits[part] = entry =>
            {
                XDocument doc;
                using (var stream = entry.Open())
                {
                    doc = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
                }

                foreach (var (site, _) in sitesInPart)
                {
                    var before = ReadCellValue(doc, entry.FullName, site, sharedStrings);
                    if (!string.Equals(before, fromState, StringComparison.Ordinal))
                    {
                        throw new WorkbookDecodeException(
                            $"CUTOVER_SITE_VALUE — the {site.Role} authority site '{site.Reference}' "
                            + $"reads '{before}', not the declared before-state '{fromState}'. The "
                            + "workbook has changed underneath this cutover. Re-read it and decide "
                            + "again rather than overwriting a value this operation did not expect: "
                            + (string.Equals(before, toState, StringComparison.Ordinal)
                                ? "this site is already at the after-state, so the cutover is at least "
                                  + "partly applied."
                                : "an unrecognised value means something else has written here."));
                    }

                    // Every check above runs while the container is still being built in memory;
                    // the file on disk is untouched until RewriteAtomically swaps it in. So a
                    // refusal anywhere leaves the workbook exactly as it was found.
                    SetCellInDocument(doc, entry.FullName, site.Row, ColumnIndex(site.PhysicalColumn),
                        site.PhysicalColumn, toState);
                    changes.Add(new DevelopmentControlCutoverSiteChange(
                        site.Role, site.Reference, before, toState));
                }

                using var buffer = new MemoryStream();
                doc.Save(buffer, SaveOptions.DisableFormatting);
                return buffer.ToArray();
            };
        }

        // Nothing has been written yet. The swap below is the single point at which the workbook
        // becomes authoritative.
        RewriteAtomically(workbookPath, edits);

        return changes;
    }

    /// <summary>
    /// TASK 1. Appends ONE record to a sheet that already exists, and returns the row it landed on.
    ///
    /// <para><b>What "already exists" rules out.</b> This creates a row in a sheet the workbook
    /// declares. It does not create a sheet, does not create a column, and does not create an
    /// envelope: the caller supplies values for columns the caller has already resolved against
    /// the form's bindings and the sheet's own header row, and every one of those resolutions is a
    /// refusal when it fails. TASK 1 requires the writer not gain arbitrary sheet-creation
    /// capability, and the mechanism that enforces it is that nothing here can invent structure —
    /// a column letter this method is handed is a letter some caller located in a header.</para>
    ///
    /// <para><b>Every value is written as an inline string.</b> Measured, not assumed: all 47,692
    /// cells of the V3 candidate are <c>t="s"</c>, so the form stores even <c>RecordVersion</c> and
    /// <c>IsCurrent</c> as text. A writer that inferred numbers would be introducing a cell type
    /// the model does not use, and the divergence would be invisible to a reader that decodes both
    /// forms — it would surface later, in whatever consumer first tried arithmetic on a column and
    /// found half of it textual.</para>
    ///
    /// <para><b>Style is inherited, not defaulted.</b> Every cell in the workbook carries a style
    /// index, so an appended row that omitted <c>s</c> would render differently from the 47,692
    /// cells above it. The index is taken from the nearest populated cell ABOVE in the same column,
    /// which is the row the new record is a continuation of.</para>
    /// </summary>
    internal static int AppendRow(
        string workbookPath, string physicalSheetName, IReadOnlyList<AppendCell> cells)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalSheetName);
        ArgumentNullException.ThrowIfNull(cells);

        if (cells.Count == 0)
        {
            throw new WorkbookDecodeException(
                "An append carrying no cells would create an empty row, and an empty row in a "
                + "governed sheet is indistinguishable from a record whose every field is blank. "
                + "Refused rather than written.");
        }

        var partName = ResolveSheetPart(workbookPath, physicalSheetName);
        var appended = -1;

        RewriteAtomically(workbookPath, partName, entry =>
        {
            var (bytes, row) = AppendRowToSheet(entry, cells);
            appended = row;
            return bytes;
        });

        return appended;
    }

    /// <summary>
    /// Builds the new container beside the target and swaps it in atomically. Shared by the cell
    /// write and the append, so the two cannot diverge in how they copy, timestamp or replace the
    /// package: the byte-determinism and atomicity properties the cross-host proof measures are
    /// properties of THIS method, and two copies of it would be two chances to lose them.
    /// </summary>
    private static void RewriteAtomically(
        string workbookPath, string partName, Func<ZipArchiveEntry, byte[]> edit)
        => RewriteAtomically(workbookPath,
            new Dictionary<string, Func<ZipArchiveEntry, byte[]>>(StringComparer.OrdinalIgnoreCase)
            {
                [partName] = edit,
            });

    /// <summary>
    /// W8D FINAL TASK 3. The same atomic swap, editing SEVERAL parts in one container build.
    ///
    /// <para>This exists because the cutover has to move four markers on four sheets and the
    /// single-part overload can only express one of them. The alternative was four sequential
    /// <see cref="RewriteAtomically(string,string,Func{ZipArchiveEntry,byte[]})"/> calls, and that
    /// is not merely slower: between any two of them the workbook on disk is authoritative through
    /// the resolver and non-authoritative on its own dashboard. A reader arriving in that window —
    /// and the lock is held, but the lock does not stop a reader — sees a self-contradicting
    /// governance record. One container build, one <see cref="File.Replace(string,string,string,bool)"/>,
    /// no window.</para>
    ///
    /// <para><b>Why the overload rather than a second copy of the method.</b> Byte-determinism and
    /// atomicity are properties of THIS code path — the cross-host proof measured them here. A
    /// parallel implementation for multi-part edits would be a second place for both to be lost,
    /// and the loss would be invisible: the workbook would still be correct, just not reproducible.</para>
    /// </summary>
    private static void RewriteAtomically(
        string workbookPath, IReadOnlyDictionary<string, Func<ZipArchiveEntry, byte[]>> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0)
        {
            throw new WorkbookDecodeException(
                "A rewrite carrying no edits would rebuild the container to change nothing. "
                + "Refused rather than performed: it would still replace the file, so the workbook's "
                + "identity would change while its content did not.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(workbookPath))
            ?? throw new WorkbookDecodeException($"'{workbookPath}' has no containing directory.");

        var temp = Path.Combine(directory,
            Path.GetFileName(workbookPath) + ".w8d-" + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        var backup = temp + ".bak";

        try
        {
            RewriteContainer(workbookPath, temp, edits);

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
    /// Builds the new container: every part copied through byte-for-byte except the target sheets,
    /// which are re-serialised with their cells changed.
    ///
    /// <para>An edit map naming a part the source does not carry is an error rather than a silent
    /// no-op. The caller believes it is changing something; a container rebuild that dropped that
    /// change would report success for a write that did not happen.</para>
    /// </summary>
    private static void RewriteContainer(
        string source, string destination, IReadOnlyDictionary<string, Func<ZipArchiveEntry, byte[]>> edits)
    {
        using var input = ZipFile.OpenRead(source);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);

        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            if (edits.TryGetValue(entry.FullName, out var edit))
            {
                applied.Add(entry.FullName);
                target.Write(edit(entry));
            }
            else
            {
                using var original = entry.Open();
                original.CopyTo(target);
            }
        }

        var missing = edits.Keys.Where(k => !applied.Contains(k)).ToArray();
        if (missing.Length > 0)
        {
            throw new WorkbookDecodeException(
                $"The container has no part {(missing.Length == 1 ? "" : "s")} "
                + string.Join(", ", missing.Select(m => $"'{m}'"))
                + ", so the requested change was not applied. Refused rather than reported as a "
                + "successful write: the caller is editing something the workbook does not carry.");
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

        SetCellInDocument(doc, entry.FullName, rowNumber, columnIndex, columnLetter, value);

        using var buffer = new MemoryStream();
        doc.Save(buffer, SaveOptions.DisableFormatting);
        return buffer.ToArray();
    }

    /// <summary>
    /// Sets one cell inside an already-loaded sheet document. Split out of
    /// <see cref="EditSheet"/> by W8D FINAL because the cutover edits several cells of the same
    /// document before serialising it once; a second copy of this logic would be a second place for
    /// the style-preservation and column-ordering rules to drift.
    /// </summary>
    private static void SetCellInDocument(
        XDocument doc, string partName, int rowNumber, int columnIndex, string columnLetter, string value)
    {
        var root = doc.Root
            ?? throw new WorkbookDecodeException($"Sheet part '{partName}' has no root element.");

        var sheetData = root.Element(Ns + "sheetData")
            ?? throw new WorkbookDecodeException($"Sheet part '{partName}' has no sheetData element.");

        var row = sheetData.Elements(Ns + "row")
            .FirstOrDefault(r => (string?)r.Attribute("r") == Invariant(rowNumber))
            ?? throw new WorkbookDecodeException(
                $"Row {rowNumber} does not exist in sheet part '{partName}'. This writer does not "
                + "create rows: a write that appends to a structure which has changed underneath it is "
                + "the failure mode this component exists to prevent. Re-read the workbook and target "
                + "a row it actually carries.");

        var reference = columnLetter + Invariant(rowNumber);
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
    }

    /// <summary>
    /// W8D FINAL TASK 3. Reads the resolved text of an authority site, so the cutover can refuse a
    /// workbook that has changed underneath it instead of overwriting a value it did not expect.
    ///
    /// <para><b>The shared string table is why this is not a one-liner.</b> Measured on the V3
    /// candidate: every one of the four sites is stored as <c>t="s"</c> — an INDEX into
    /// <c>xl/sharedStrings.xml</c> — so the raw <c>&lt;v&gt;</c> text of <c>00_Control!B10</c> is a
    /// number, not <c>CANDIDATE</c>. A guard comparing that number against the expected state would
    /// refuse every well-formed workbook, and a guard that skipped the comparison when the text
    /// looked numeric would silently stop checking the one cell that decides. Both forms are
    /// resolved here because the writer produces the other one: a cell this component has already
    /// written is <c>t="inlineStr"</c>.</para>
    ///
    /// <para>Throws <see cref="WorkbookDecodeException"/> when the row or the cell is absent. An
    /// absent site is not an empty site: a workbook that has lost a marker cannot be promoted from
    /// the state that marker was supposed to carry.</para>
    /// </summary>
    private static string ReadCellValue(
        XDocument doc, string partName, DevelopmentControlAuthoritySites.Site site,
        IReadOnlyList<string> sharedStrings)
    {
        var root = doc.Root
            ?? throw new WorkbookDecodeException($"Sheet part '{partName}' has no root element.");

        var sheetData = root.Element(Ns + "sheetData")
            ?? throw new WorkbookDecodeException($"Sheet part '{partName}' has no sheetData element.");

        var row = sheetData.Elements(Ns + "row")
            .FirstOrDefault(r => (string?)r.Attribute("r") == Invariant(site.Row))
            ?? throw new WorkbookDecodeException(
                $"CUTOVER_SITE_MISSING — row {site.Row} does not exist in sheet part '{partName}', so "
                + $"the {site.Role} authority site '{site.Reference}' cannot be read.");

        var reference = site.PhysicalColumn + Invariant(site.Row);
        var cell = row.Elements(Ns + "c")
            .FirstOrDefault(c => string.Equals((string?)c.Attribute("r"), reference, StringComparison.OrdinalIgnoreCase))
            ?? throw new WorkbookDecodeException(
                $"CUTOVER_SITE_MISSING — the {site.Role} authority site '{site.Reference}' is absent "
                + $"from sheet part '{partName}'.");

        // t="s" -> an index into the shared table; t="inlineStr" -> the text is in the cell.
        var value = cell.Element(Ns + "v");
        if (value is not null)
        {
            if (string.Equals((string?)cell.Attribute("t"), "s", StringComparison.Ordinal)
                && int.TryParse(value.Value, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var index))
            {
                if (index < 0 || index >= sharedStrings.Count)
                {
                    throw new WorkbookDecodeException(
                        $"CUTOVER_SITE_UNREADABLE — the {site.Role} authority site '{site.Reference}' "
                        + $"indexes shared string {index}, which the table (holding "
                        + $"{sharedStrings.Count}) does not contain. The workbook is internally "
                        + "inconsistent, so its authority state cannot be established.");
                }

                return sharedStrings[index];
            }

            return value.Value;
        }

        var inline = cell.Element(Ns + "is");
        if (inline is not null)
            return string.Concat(inline.Descendants(Ns + "t").Select(t => t.Value));

        return string.Empty;
    }

    /// <summary>
    /// The workbook's shared string table, in index order, or empty when it declares none.
    /// Read once per cutover rather than per site: it is the largest part of the package (6,109
    /// entries on the V3 candidate) and re-parsing it four times would be four chances to read a
    /// different table than the sheet being checked.
    /// </summary>
    private static IReadOnlyList<string> ReadSharedStrings(string workbookPath)
    {
        using var zip = ZipFile.OpenRead(workbookPath);

        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return Array.Empty<string>();

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);

        return doc.Root is null
            ? Array.Empty<string>()
            : doc.Root.Elements(Ns + "si")
                .Select(si => string.Concat(si.Descendants(Ns + "t").Select(t => t.Value)))
                .ToArray();
    }

    /// <summary>
    /// Loads the sheet part, appends one row beneath the last one, widens the sheet's
    /// <c>&lt;dimension&gt;</c> to cover it, and returns the bytes plus the row number used.
    ///
    /// <para><b>Three structural properties are maintained rather than assumed.</b> Each was
    /// measured on the V3 candidate first, because each is the kind of invariant that holds
    /// silently until the one workbook where it does not:</para>
    /// <list type="number">
    /// <item><description><b><c>spans</c>.</b> Every one of the 26 sheets marks every one of its
    /// <c>&lt;row&gt;</c> elements with <c>spans</c>, and the value always equals the sheet's full
    /// column width. The new row inherits the last row's <c>spans</c>; omitting it would make the
    /// appended row the only one without a span hint.</description></item>
    /// <item><description><b>Style.</b> Every cell in the workbook carries a style index. The new
    /// cells inherit the index of the nearest populated cell above them in the same column. A cell
    /// with no styled ancestor above it is written with no index rather than a guessed one, because
    /// a wrong index restyles the record and a missing one merely renders as the sheet default.</description></item>
    /// <item><description><b><c>&lt;dimension&gt;</c>.</b> All 26 sheets declare one, and a
    /// <c>&lt;dimension&gt;</c> that stops short of a row the sheet actually carries is a range
    /// integrity defect: consumers that trust it — including several that read only the dimension
    /// to size a sheet — would not see the appended record. The end reference is widened; the start
    /// reference is left exactly as found.</description></item>
    /// </list>
    ///
    /// <para><b>Merged ranges are checked, not assumed absent.</b> Measured: the V3 candidate
    /// declares no <c>&lt;mergeCells&gt;</c> at all. The guard is still implemented, because
    /// appending a row INTO a merged range produces a workbook Excel offers to repair, and that
    /// damage is invisible to a name-based reader — the record reads back perfectly while the file
    /// is corrupt. An invariant that is only true of today's artifact is a coincidence, and this is
    /// the one place the component can turn it into a check.</para>
    /// </summary>
    private static (byte[] Bytes, int Row) AppendRowToSheet(
        ZipArchiveEntry entry, IReadOnlyList<AppendCell> cells)
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

        var rows = sheetData.Elements(Ns + "row").ToList();
        if (rows.Count == 0)
        {
            throw new WorkbookDecodeException(
                $"Sheet part '{entry.FullName}' carries no rows, so it has no header row for an "
                + "appended record to sit beneath. A sheet with no header is not a table this "
                + "writer may add a record to.");
        }

        var lastRowNumber = rows.Select(ParseRowNumber).Where(n => n is not null)
            .Select(n => n!.Value).DefaultIfEmpty(0).Max();

        if (lastRowNumber == 0)
        {
            throw new WorkbookDecodeException(
                $"No row in sheet part '{entry.FullName}' declares a row number, so there is no "
                + "position to append at. The row-number attribute is what the reader locates "
                + "records by, and inventing one here would desynchronise the two.");
        }

        var appendedRow = lastRowNumber + 1;

        var merges = root.Element(Ns + "mergeCells");
        if (merges is not null)
        {
            foreach (var merge in merges.Elements(Ns + "mergeCell"))
            {
                var range = (string?)merge.Attribute("ref");
                if (range is null || !RangeContainsRow(range, appendedRow)) continue;

                throw new WorkbookDecodeException(
                    $"Appending row {appendedRow} to sheet part '{entry.FullName}' would land inside "
                    + $"merged range '{range}'. A record written into a merged range is a workbook "
                    + "corruption that still reads back correctly by name, so it is refused rather "
                    + "than written and detected later.");
            }
        }

        var previousRow = rows.First(r => ParseRowNumber(r) == lastRowNumber);

        var newRow = new XElement(Ns + "row", new XAttribute("r", Invariant(appendedRow)));

        var spans = (string?)previousRow.Attribute("spans");
        if (spans is not null) newRow.SetAttributeValue("spans", spans);

        // Ascending column order is required by OOXML; a row whose cells are out of order is
        // rejected by stricter consumers even though this reader would resolve it by name.
        foreach (var cell in cells.OrderBy(c => ColumnIndex(c.ColumnLetter)))
        {
            var reference = cell.ColumnLetter.ToUpperInvariant() + Invariant(appendedRow);

            var element = new XElement(Ns + "c",
                new XAttribute("r", reference),
                new XAttribute("t", "inlineStr"));

            var style = NearestStyleAbove(rows, cell.ColumnLetter);
            if (style is not null) element.SetAttributeValue("s", style);

            element.Add(new XElement(Ns + "is", new XElement(Ns + "t", cell.Value)));
            newRow.Add(element);
        }

        previousRow.AddAfterSelf(newRow);

        WidenDimension(root, appendedRow,
            cells.Select(c => ColumnIndex(c.ColumnLetter)).DefaultIfEmpty(0).Max());

        using var buffer = new MemoryStream();
        doc.Save(buffer, SaveOptions.DisableFormatting);
        return (buffer.ToArray(), appendedRow);
    }

    /// <summary>
    /// Grows the sheet's <c>&lt;dimension&gt;</c> end reference to cover <paramref name="appendedRow"/>
    /// and <paramref name="appendedColumnIndex"/>, leaving the start reference untouched.
    ///
    /// <para>Does nothing when the sheet declares no <c>&lt;dimension&gt;</c>. Creating one would be
    /// inventing structure in a form that chose not to state it, and the safe direction here is
    /// asymmetry: a missing dimension costs a consumer a full scan, while a WRONG one makes it
    /// confidently skip records.</para>
    /// </summary>
    private static void WidenDimension(XElement root, int appendedRow, int appendedColumnIndex)
    {
        var dimension = root.Element(Ns + "dimension");
        var reference = (string?)dimension?.Attribute("ref");
        if (dimension is null || string.IsNullOrWhiteSpace(reference)) return;

        var parts = reference.Split(':');
        var start = parts[0];
        var end = parts.Length > 1 ? parts[1] : parts[0];

        var startRow = ParseRowNumberFromReference(start) ?? 1;
        var endRow = ParseRowNumberFromReference(end) ?? startRow;
        var startColumn = ColumnLetters(start);
        var endColumn = ColumnLetters(end);

        var newEndRow = Math.Max(endRow, appendedRow);
        var newEndColumn = ColumnIndex(endColumn) >= appendedColumnIndex ? endColumn : ColumnLettersOf(appendedColumnIndex);

        dimension.SetAttributeValue("ref",
            $"{startColumn}{Invariant(startRow)}:{newEndColumn}{Invariant(newEndRow)}");
    }

    /// <summary>
    /// The style index of the nearest populated cell ABOVE in the given column, or null when no
    /// such cell carries one. Scans from the bottom so the appended record continues the styling of
    /// the row it follows rather than of some earlier era of the sheet.
    /// </summary>
    private static string? NearestStyleAbove(IReadOnlyList<XElement> rows, string columnLetter)
    {
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            foreach (var cell in rows[i].Elements(Ns + "c"))
            {
                var reference = (string?)cell.Attribute("r");
                if (reference is null) continue;
                if (!string.Equals(ColumnLetters(reference), columnLetter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (cell.Attribute("s") is { } style) return style.Value;
            }
        }

        return null;
    }

    /// <summary>True when the A1-style range contains the given row. Used by the merge guard.</summary>
    private static bool RangeContainsRow(string range, int row)
    {
        var parts = range.Split(':');
        var first = ParseRowNumberFromReference(parts[0]);
        var last = parts.Length > 1 ? ParseRowNumberFromReference(parts[1]) : first;
        if (first is null || last is null) return false;
        return row >= first.Value && row <= last.Value;
    }

    private static int? ParseRowNumber(XElement row) => ParseRowNumberFromReference((string?)row.Attribute("r"));

    /// <summary>"AB12" → 12. Null when the reference carries no row, which is a malformed sheet.</summary>
    private static int? ParseRowNumberFromReference(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;

        var digits = new string(reference.SkipWhile(char.IsLetter).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>28 → "AB" (1-based). The inverse of <see cref="ColumnIndex"/>.</summary>
    internal static string ColumnLettersOf(int columnIndex)
    {
        var letters = "";
        while (columnIndex > 0)
        {
            var remainder = (columnIndex - 1) % 26;
            letters = (char)('A' + remainder) + letters;
            columnIndex = (columnIndex - 1) / 26;
        }

        return letters;
    }

    private static string Invariant(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

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

/// <summary>
/// One cell of an appended row, addressed by PHYSICAL column letter.
///
/// <para>This type is <c>internal</c> and stays behind the contract boundary on purpose. TASK 2
/// forbids exposing spreadsheet mechanics as a cross-host business contract, and a column letter is
/// exactly that: it is how this component addresses a cell, not how a caller thinks about a
/// governance record. The public surface names logical sheets and logical columns and lets the
/// reservation resolve them; by the time a value reaches this type the resolution has already
/// happened, and the letter is a resolved fact rather than a caller's guess.</para>
/// </summary>
internal sealed record AppendCell(string ColumnLetter, string Value);
