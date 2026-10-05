namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// W10.0A FINAL TASK 4 — <b>the three workbooks this suite needs, generated once per test run.</b>
///
/// <para>
/// <b>What changed, and what deliberately did not.</b> Before this, the three sources were the estate's
/// own files: the live canonical authority, a preserved pre-cutover candidate revision, and a preserved
/// 14-sheet legacy revision. They are now generated. <b>No test's assertions were relaxed to make that
/// work</b> — that is the whole point of generating a faithful structure rather than loosening a check.
/// What the tests assert about a workbook is its FORM, its AUTHORITY and how the append path treats it;
/// all three are properties of the schema, and the fixture carries them.
/// </para>
///
/// <para>
/// <b>Why one shared instance rather than one per test class.</b> A fixture that is built once is
/// built from one definition. Two classes each building their own would be two definitions that can
/// drift — and the drift would show up as one suite passing and the other failing on the same
/// behaviour, which is far more expensive to diagnose than the shared instance costs.
/// </para>
///
/// <para>
/// <b>Lifetime.</b> Generated under the process's temp directory on first use and deleted when the
/// process exits. Nothing here writes to a named estate root, and nothing here is committed.
/// </para>
/// </summary>
internal static class WorkbookFixtures
{
    private static readonly Lazy<string> Directory = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "nexus-dc-fixtures-" + Guid.NewGuid().ToString("N")[..12]);
        System.IO.Directory.CreateDirectory(path);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { System.IO.Directory.Delete(path, recursive: true); } catch { /* temp; best effort */ }
        };

        return path;
    });

    /// <summary>
    /// The V3 candidate: four authority sites reading <c>CANDIDATE</c>. The only state in which the
    /// cutover's positive path can run, because the component correctly refuses to promote a workbook
    /// that is already authoritative.
    /// </summary>
    public static string Candidate => Field("v3-candidate.xlsx", WorkbookFixtureBuilder.Candidate);

    /// <summary>The V3 authority: the same workbook with all four sites reading <c>AUTHORITATIVE</c>.</summary>
    public static string Authoritative => Field("v3-authoritative.xlsx", WorkbookFixtureBuilder.Authoritative);

    /// <summary>
    /// The legacy form. Identified by sheet NAMES — <c>Control Center</c> and <c>Master Roadmap</c> —
    /// never by a sheet count, so this carries exactly those two.
    /// </summary>
    public static string Legacy => Field("legacy-14-sheet.xlsx", WorkbookFixtureBuilder.Legacy);

    private static readonly Dictionary<string, string> Built = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    private static string Field(string fileName, Func<string, string> build)
    {
        lock (Gate)
        {
            if (Built.TryGetValue(fileName, out var existing))
            {
                return existing;
            }

            var path = build(Path.Combine(Directory.Value, fileName));
            Built[fileName] = path;
            return path;
        }
    }
}
