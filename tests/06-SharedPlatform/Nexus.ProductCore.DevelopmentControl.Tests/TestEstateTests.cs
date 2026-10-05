using Xunit;

namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// W10.0A FINAL TASK 6 — <b>negative controls for the governed test root.</b>
///
/// <para>
/// <see cref="TestEstate"/> decides which estate the suite reads from, and it decides it from an
/// environment variable. A resolver that is only reachable through process-wide state is a resolver
/// no test can exercise — which is exactly how the previous arrangement came to have three
/// unvalidated variable reads that nobody had ever seen refuse anything.
/// </para>
///
/// <para>
/// So each refusal is controlled here directly, and each is paired with the case that must be
/// accepted: a validator that refused everything would satisfy every "is refused" assertion below
/// while making the suite unrunnable, and these tests would not notice.
/// </para>
///
/// <para>
/// <b>These controls are portable by construction.</b> They build their own directories under the
/// temp path and call the validator on strings. Nothing here needs an estate, a workbook or a lock —
/// which matters, because a control for the mechanism that makes the suite portable should not itself
/// be the thing that stops it being portable.
/// </para>
/// </summary>
public sealed class TestEstateTests
{
    [Fact]
    public void ARootedPath_IsAccepted()
    {
        var rooted = Path.GetTempPath();

        var validated = TestEstate.ValidateForTest(rooted);

        Assert.Equal(
            Path.GetFullPath(rooted).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            validated);
    }

    /// <summary>
    /// A relative root is refused rather than resolved against the working directory. This is the
    /// same rule the component applies to store paths (M1D vector V14 / F-2): a path whose meaning
    /// depends on where the process started is not an identity, and an estate chosen that way would
    /// differ between two runs of the same suite without either run saying so.
    /// </summary>
    [Theory]
    [InlineData("relative/estate")]
    [InlineData("./here")]
    [InlineData("../up-one")]
    [InlineData("")]
    public void ARelativeOrEmptyRoot_IsRefused(string root)
    {
        Assert.Throws<InvalidOperationException>(() => TestEstate.ValidateForTest(root));
    }

    /// <summary>
    /// <b>The trap this closes: discovery by walking up to the nearest repository.</b> A root that is
    /// merely INSIDE a Git work tree inherits that repository's identity and contents. A suite pointed
    /// at such a path reports confidently about files it never opened — and the failure is silent,
    /// because everything it looks for is usually absent and "absent" is a plausible answer.
    ///
    /// <para>
    /// The control builds a real nested Git marker under the temp directory, so the refusal is proved
    /// against the actual condition rather than against a string that merely looks like a path.
    /// </para>
    /// </summary>
    [Fact]
    public void ARootInsideAnotherGitWorkTree_IsRefused()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "w10-testestate-" + Guid.NewGuid().ToString("N")[..10]);
        var outer = Path.Combine(scratch, "some-repository");
        var inner = Path.Combine(outer, "nested", "estate");

        Directory.CreateDirectory(Path.Combine(outer, ".git"));
        Directory.CreateDirectory(inner);

        try
        {
            var thrown = Assert.Throws<InvalidOperationException>(() => TestEstate.ValidateForTest(inner));

            Assert.Contains("inside the Git work tree", thrown.Message, StringComparison.Ordinal);
            Assert.Contains(outer, thrown.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* temp; best effort */ }
        }
    }

    /// <summary>
    /// The positive twin of the control above, and the assertion that keeps it from being over-broad:
    /// a directory that IS the root of its own work tree is accepted. Without this, "refuses a nested
    /// root" would also pass on a validator that refused every directory containing a `.git`
    /// anywhere above it — which would rule out the estate root itself.
    /// </summary>
    [Fact]
    public void AWorkTreeRootItself_IsAccepted()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "w10-testestate-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Path.Combine(scratch, ".git"));

        try
        {
            var validated = TestEstate.ValidateForTest(scratch);

            Assert.Equal(Path.GetFullPath(scratch).TrimEnd(Path.DirectorySeparatorChar), validated);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* temp; best effort */ }
        }
    }

    /// <summary>
    /// With nothing configured the suite reads the canonical estate root — a statement about where
    /// the estate lives, kept separate from whether it is present. Tests that prove contract
    /// behaviour never consult either, because their workbook is generated.
    /// </summary>
    [Fact]
    public void WithNothingConfigured_TheCanonicalEstateRootIsNamed()
    {
        // The variable is read once at type initialisation, so this asserts the composed value rather
        // than re-reading it: if a root WAS configured, the configured one must be in force.
        if (TestEstate.IsExplicitlyConfigured)
        {
            Assert.NotEqual(TestEstate.CanonicalEstateRoot, TestEstate.Root);
        }
        else
        {
            Assert.Equal(TestEstate.CanonicalEstateRoot, TestEstate.Root);
        }
    }
}
