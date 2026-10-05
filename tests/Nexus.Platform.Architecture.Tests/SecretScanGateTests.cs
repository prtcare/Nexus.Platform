using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Platform.Architecture.Tests;

/// <summary>
/// The W9 build gate: this repository scans itself for committed secrets, and fails the build on a match.
///
/// <para>
/// <b>Why this test is the gate rather than a pipeline step.</b> Platform's CI runs the whole solution
/// with no <c>--filter</c>, deliberately, so the architecture tests act as the repository's hard
/// boundary gate. A scan placed here inherits that: it runs on every push and pull request, it cannot
/// be skipped by editing a workflow file, and its failure is a build failure. W9.0 recorded the control
/// this replaces — <c>CONFIGURATION_STANDARDS.md</c> §13.1, "CURRENT: nothing enforces this. There is no
/// CI, therefore no secret scan", whose target milestone M-01-5.1 specifies exactly this: <i>"a secret
/// scan runs in CI and fails the build on a match."</i>
/// </para>
///
/// <para>
/// <b>The three verdicts are kept distinct.</b> <c>Clean</c> passes; <c>Findings</c> fails with a
/// located list; <c>Incomplete</c> also fails, because a scan that could not read part of its input has
/// not established that the repository is clean, and a gate that goes green when it did not run is the
/// defect class this estate has now recorded three times.
/// </para>
///
/// <para>
/// Findings name a path, a line, a rule, a key and a length. <b>No part of any matched value is printed,
/// passed to an assertion message, or written to test output</b> — a build log is the wrong place for a
/// partial credential, and a partial credential reads as a real one to whoever finds it next.
/// </para>
/// </summary>
public sealed class SecretScanGateTests
{
    private readonly ITestOutputHelper _output;

    public SecretScanGateTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The gate. Scans this repository's tree with the neutral policy — no provider patterns, because
    /// Platform's neutral assemblies may not name provider secret semantics at all
    /// (<see cref="SecretBoundaryTests"/> fails the build if they do).
    /// </summary>
    [Fact]
    public void Repository_ContainsNoCommittedSecret()
    {
        var root = RepositoryRoot();

        var result = new SecretScanner().ScanDirectory(root);

        foreach (var line in result.Describe())
        {
            _output.WriteLine(line);
        }

        _output.WriteLine(
            $"verdict={result.Verdict} scanned={result.FilesScanned} skipped={result.FilesSkipped} "
            + $"findings={result.Findings.Count} incomplete={result.IncompleteReasons.Count}");

        Assert.True(
            result.Verdict == SecretScanVerdict.Clean,
            $"The secret scan was not clean (verdict {result.Verdict}). "
            + $"{result.Findings.Count} finding(s) and {result.IncompleteReasons.Count} incomplete reason(s) "
            + "are listed above. A committed credential must be ROTATED first (CONFIGURATION_STANDARDS.md "
            + "§13.2 — removing the file does not remove it from history), then removed, then the scan re-run.");
    }

    /// <summary>
    /// Non-vacuity companion. If the scanner cannot find a credential in a tree that provably contains
    /// one, the gate above is worthless — so the gate proves the detector fires on the same code path,
    /// against a fixture written at run time. The value is generated, never a literal, so it cannot
    /// itself become something this repository has committed.
    /// </summary>
    [Fact]
    public void TheGateIsNonVacuous_OnAFixtureThatContainsACredential()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexus-w91-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            // Same shape as the finding that motivated this gate: a vendor-prefixed key assigned to a
            // credential-named variable. Generated, not copied.
            var synthetic = "sk-" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];
            File.WriteAllText(Path.Combine(root, "profile.ps1"), $"$env:PROVIDER_AUTH_TOKEN=\"{synthetic}\"");

            var result = new SecretScanner().ScanDirectory(root);

            Assert.Equal(SecretScanVerdict.Findings, result.Verdict);
            Assert.NotEmpty(result.Findings);

            // And the finding does not carry the value.
            Assert.DoesNotContain(synthetic, string.Join("\n", result.Describe()), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Locates the repository root by walking up for the solution file. Resolved from the source tree
    /// rather than from a hard-coded absolute path, so the gate works in CI, in a worktree, and on a
    /// developer's machine without a configuration file that could drift.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Nexus.Platform.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root: no Nexus.Platform.slnx was found in any ancestor of "
            + AppContext.BaseDirectory + ". The secret-scan gate cannot run, and a gate that cannot run "
            + "must not report success.");
    }
}
