using Nexus.DevelopmentControl.Safety;
using Xunit;

namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// The authority-substitution refusal — proof #11 of the W8D authority cutover: "Product-local /
/// legacy / C:\Personal workbook fallback refused".
///
/// <para><b>Why this file exists.</b> <see cref="WorkbookCompatibilityReader.ResolveAuthority"/> and
/// <see cref="WorkbookCompatibilityReader.AuthorizeWritePath"/> landed in W8D-R4 (<c>b845284</c>) and
/// shipped with no test at all. A guard for the single most consequential question in the estate —
/// *which file is the authority* — was therefore unverified: nothing would have failed if it had
/// silently started returning the product-local workbook. These tests are that missing proof, and
/// they are the reason the cutover can claim #11 rather than assert it.</para>
///
/// <para><b>What is actually being proved.</b> Not "a string contains a word". Each refusal test
/// feeds a path that a permissive resolver would happily accept — a real workbook, of the right
/// schema, in the right estate — and asserts it is refused anyway, with a reason that NAMES the
/// substitute that was not chosen. The negative controls at the end keep the guard from being
/// proved by being over-broad: a disposable path must still be writable, or every fixture proof in
/// the estate would break and "refused" would mean nothing.</para>
///
/// <para><b>These tests never touch the filesystem for the refusal cases.</b> The path-class checks
/// are ordered before the existence check, so a refused path is refused without being opened. That
/// matters for the <c>C:\Personal</c> case in particular: the point is that no read is attempted
/// there, not that a read fails.</para>
/// </summary>
public sealed class AuthorityResolutionTests
{
    private const string Canonical = WorkbookCompatibilityMap.CanonicalAuthorityPath;

    /// <summary>The product-local workbook — which is also the preserved 14-sheet legacy revision.</summary>
    private const string ProductLocal = WorkbookCompatibilityMap.LegacyAuthorityPath;

    private const string PersonalCopy = @"C:\Personal\Nexus.Developer\NEXUS_DEVELOPMENT_CONTROL.xlsx";

    /// <summary>
    /// The positive half, and the control for every refusal below: the canonical path is ACCEPTED.
    /// Without this, a resolver that refused everything would pass all the other tests in this file.
    /// </summary>
    // W10.0A FINAL — LANE: EstateHost. This asserts a property of the LIVE canonical workbook, so it
    // can only hold where that file exists. The refusals below are portable and stay in the portable
    // lane; this pair is the positive control that keeps them from being vacuous, so it must keep
    // running — in the lane whose subject it actually is. See PORTABLE_TEST_ARCHITECTURE.md.
    [Fact]
    [Trait("Lane", "EstateHost")]
    public void TheCanonicalAuthority_ResolvesAsAvailable()
    {
        var resolution = WorkbookCompatibilityReader.ResolveAuthority(Canonical);

        Assert.True(resolution.Available, resolution.Reason);
        Assert.Equal(Canonical, resolution.Path);
        Assert.Equal(Canonical, resolution.ExpectedPath);

        // The reason reports what it read, not merely that it succeeded. Since the W8D cutover the
        // canonical workbook is the authority, so the marker it reports is AUTHORITATIVE.
        Assert.Contains("AUTHORITATIVE", resolution.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("no fallback is chosen", resolution.Reason, StringComparison.Ordinal);
    }

    /// <summary>A path spelled with forward slashes and different case is not a bypass.</summary>
    [Fact]
    [Trait("Lane", "EstateHost")]
    public void TheCanonicalAuthority_ResolvesTheSame_UnderAMixedSpelling()
    {
        var resolution = WorkbookCompatibilityReader.ResolveAuthority(
            "d:/nexus/developmentcontrol/NEXUS_DEVELOPMENT_CONTROL.XLSX");

        Assert.True(resolution.Available, resolution.Reason);
    }

    [Fact]
    public void TheProductLocalWorkbook_IsRefusedAsASubstitute()
    {
        var resolution = WorkbookCompatibilityReader.ResolveAuthority(ProductLocal);

        Assert.False(resolution.Available);
        Assert.Equal(ProductLocal, resolution.Path);
        Assert.Equal(Canonical, resolution.ExpectedPath);      // it still says where the authority IS
        Assert.Contains("AUTHORITY_UNAVAILABLE", resolution.Reason, StringComparison.Ordinal);
        Assert.Contains("no fallback is chosen", resolution.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void APersonalCopy_IsRefusedAsASubstitute()
    {
        var resolution = WorkbookCompatibilityReader.ResolveAuthority(PersonalCopy);

        Assert.False(resolution.Available);
        Assert.Contains("AUTHORITY_UNAVAILABLE", resolution.Reason, StringComparison.Ordinal);
        Assert.Contains(WorkbookCompatibilityMap.PersonalRootPrefix, resolution.Reason, StringComparison.Ordinal);
        Assert.Contains("no fallback is chosen", resolution.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NoConfiguredPath_IsRefused_AndStillNamesTheAuthority()
    {
        foreach (var blank in new[] { null, "", "   " })
        {
            var resolution = WorkbookCompatibilityReader.ResolveAuthority(blank!);

            Assert.False(resolution.Available);
            Assert.Equal(Canonical, resolution.ExpectedPath);
            Assert.Contains("no fallback is chosen", resolution.Reason, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A copy of the real authority, placed anywhere else, is STILL refused. This is the case a
    /// path-only check could be fooled by: the file is byte-identical to the authority, so a
    /// resolver that judged by content would accept it. The rule is about the path, deliberately.
    /// </summary>
    [Fact]
    public void ACopyOfTheAuthority_Elsewhere_IsRefused()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "NEXUS_DEVELOPMENT_CONTROL.xlsx");

        var resolution = WorkbookCompatibilityReader.ResolveAuthority(elsewhere);

        Assert.False(resolution.Available);
        Assert.Contains("AUTHORITY_UNAVAILABLE", resolution.Reason, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the write-path class refusal

    [Fact]
    public void AuthorizeWritePath_RefusesTheLegacyWorkbook_AsPreservedNotDeleted()
    {
        var (allowed, reason) = WorkbookCompatibilityReader.AuthorizeWritePath(ProductLocal);

        Assert.False(allowed);
        Assert.Contains(WorkbookCompatibilityReader.LegacyReadOnlyDesignation, reason, StringComparison.Ordinal);
        // The Owner's directive is that the legacy revisions are PRESERVED. The refusal has to say
        // so, or the next reader may "tidy up" a file the directive forbids deleting.
        Assert.Contains("PRESERVED", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorizeWritePath_RefusesAPersonalPath()
    {
        var (allowed, reason) = WorkbookCompatibilityReader.AuthorizeWritePath(PersonalCopy);

        Assert.False(allowed);
        Assert.Contains("AUTHORITY_UNAVAILABLE", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The negative control. A disposable path must remain writable, or the refusal above would be
    /// indistinguishable from a guard that refuses everything — and every fixture proof in the
    /// estate that writes to a temp copy would be asserting a refusal it never noticed.
    /// </summary>
    [Fact]
    public void AuthorizeWritePath_PermitsADisposablePath()
    {
        var disposable = Path.Combine(Path.GetTempPath(),
            "w8d-authority-probe-" + Guid.NewGuid().ToString("N")[..10], "NEXUS_DEVELOPMENT_CONTROL.xlsx");

        var (allowed, reason) = WorkbookCompatibilityReader.AuthorizeWritePath(disposable);

        Assert.True(allowed, reason);
    }

    /// <summary>The canonical authority is itself writable by a governed writer — the guard refuses
    /// the SUBSTITUTES, not the authority.</summary>
    [Fact]
    public void AuthorizeWritePath_PermitsTheCanonicalAuthority()
    {
        var (allowed, reason) = WorkbookCompatibilityReader.AuthorizeWritePath(Canonical);

        Assert.True(allowed, reason);
    }
}
