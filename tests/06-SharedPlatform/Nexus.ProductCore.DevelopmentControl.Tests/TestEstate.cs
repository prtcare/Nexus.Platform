namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// W10.0A FINAL TASKS 2 and 6 — <b>the one place in this suite that decides which estate a test is
/// reading from.</b>
///
/// <para>
/// <b>The defect this closes.</b> Three workbook sources were resolved by three separate
/// <c>Environment.GetEnvironmentVariable</c> reads scattered across two test files, each falling back
/// to a hardcoded <c>D:\NEXUS\…</c> path. Nothing validated what the variable contained, nothing
/// distinguished "the estate is not present" from "the estate is present and wrong", and every test
/// that touched a workbook therefore required one developer's machine. On the Linux CI runner 81 tests
/// failed with <c>FileNotFoundException</c> — a property of where they ran, not of the component.
/// </para>
///
/// <para>
/// <b>The rule, in one line.</b> Production keeps its canonical estate root as a default; <b>tests
/// get a deterministic synthetic fixture unless a root is explicitly named, and that root is
/// validated before use.</b> There is no ambient discovery and no fallback: a test never silently
/// reads a different estate than the one it was pointed at.
/// </para>
///
/// <para>
/// <b>What is validated, and why each matters.</b>
/// </para>
/// <list type="bullet">
/// <item><description><b>Non-empty and rooted.</b> A relative root would resolve against whatever
/// directory the test host happened to start in — the very class of defect (<c>F-2</c>, M1D vector
/// V14) that <c>DevelopmentControlStoreIdentity</c> exists to refuse for store paths. The same rule
/// applies to choosing an estate.</description></item>
/// <item><description><b>Not a fallback to an arbitrary parent Git repository.</b> Discovering the
/// root by walking up until a <c>.git</c> appears is how a test silently starts reading the repository
/// it lives in and reports green about a workbook it never opened. That is refused, not tolerated: a
/// root whose own directory is inside a Git work tree that is not itself the root is rejected.</description></item>
/// <item><description><b>Read-only in practice.</b> Every fixture COPIES the source into a throwaway
/// directory before a test touches it, and <see cref="AssertReadable"/> opens for read only. No test
/// in this suite writes to a named root.</description></item>
/// </list>
/// </summary>
internal static class TestEstate
{
    /// <summary>The canonical estate root — the production default, named once.</summary>
    public const string CanonicalEstateRoot = @"D:\NEXUS";

    /// <summary>The only environment variable this suite reads for a root.</summary>
    public const string RootVariable = "NEXUS_TEST_ESTATE_ROOT";

    /// <summary>
    /// The explicitly configured root, or <c>null</c>. <b>The single environment read in this
    /// assembly.</b> Every other member goes through it, so an override is one decision rather than
    /// four that can disagree.
    /// </summary>
    private static readonly string? Configured = Resolve();

    private static string? Resolve()
    {
        var value = Environment.GetEnvironmentVariable(RootVariable);

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Validate(value.Trim(), RootVariable);
    }

    /// <summary>
    /// Validates a candidate root. Throws rather than falling back — a misconfigured root must stop
    /// the run, because the alternative is a suite that reports on an estate nobody selected.
    /// </summary>
    /// <summary>
    /// The validator, exposed so it can be controlled directly. A guard for "which estate am I
    /// reading" that is only reachable through a process-wide environment variable would be a guard
    /// no test could exercise — and this suite's whole reason for existing is that unexercised
    /// guards are the defect class it keeps finding.
    /// </summary>
    internal static string ValidateForTest(string root, string source = "test") => Validate(root, source);

    private static string Validate(string root, string source)
    {
        if (!Path.IsPathRooted(root))
        {
            throw new InvalidOperationException(
                $"{source} was set to '{root}', which is not an absolute path. A relative estate root "
                + "resolves against the test host's working directory, so two runs of the same suite "
                + "could read two different estates and neither would say so. Give a rooted path, or "
                + "leave the variable unset to use the synthetic fixtures.");
        }

        // Must not resolve to a directory that merely HAPPENS to sit inside some Git work tree. The
        // root's own directory must not be inside a repository it is not the root of — otherwise a
        // mistyped path silently reads a neighbouring checkout.
        var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var probe = new DirectoryInfo(full); probe?.Parent is not null; probe = probe.Parent)
        {
            if (Directory.Exists(Path.Combine(probe.FullName, ".git")))
            {
                if (!string.Equals(probe.FullName.TrimEnd(Path.DirectorySeparatorChar), full, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"{source} was set to '{root}', which is inside the Git work tree "
                        + $"'{probe.FullName}' rather than being a root itself. Refusing it: a root "
                        + "found by walking up to the nearest repository is how a suite comes to read "
                        + "the checkout it lives in and report green about a workbook it never opened.");
                }

                break;
            }
        }

        return full;
    }

    /// <summary>True when a root was explicitly named and passed validation.</summary>
    public static bool IsExplicitlyConfigured => Configured is not null;

    /// <summary>
    /// The root the suite will read from. When no root is named this is the canonical estate root —
    /// which is a statement about where the estate lives, not a claim that it is present.
    /// </summary>
    public static string Root => Configured ?? CanonicalEstateRoot;

    /// <summary>
    /// True when the live estate is actually readable at <see cref="Root"/>. Tests whose subject IS
    /// the real estate assert on this; tests proving contract behaviour never consult it, because
    /// they use a fixture that is generated and therefore always present.
    /// </summary>
    public static bool LiveEstateAvailable =>
        File.Exists(Path.Combine(Root, "DevelopmentControl", "NEXUS_DEVELOPMENT_CONTROL.xlsx"));

    /// <summary>Opens a path for read and fails with a located message if it is not there.</summary>
    public static FileStream AssertReadable(string path, string what)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{what} was not found at '{path}'. This suite resolves it through {nameof(TestEstate)} "
                + $"from root '{Root}'"
                + (IsExplicitlyConfigured ? $" (set by {RootVariable})" : " (the canonical estate default)")
                + ". Tests that prove contract behaviour must not depend on the live estate — see "
                + "WorkbookFixtureBuilder.",
                path);
        }

        return File.OpenRead(path);
    }
}
