using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Nexus.Platform.Architecture.Tests;

/// <summary>
/// W10.0A TASK 7: the solution must not be able to hide a project.
///
/// <para>
/// <b>The defect this closes, measured twice in this estate.</b> A project that exists on disk but
/// is not listed in <c>Nexus.Platform.slnx</c> is built by nothing. Its <c>ProjectReference</c>s are
/// resolved by nothing, so when a referenced project moves, the dangling edge is never surfaced:
/// the estate build stays green and the orphan is discovered later, by a human, by accident. This
/// is exactly what happened to <c>samples/Nexus.Platform.SmokeHost</c> — W5G moved the OpenAI
/// provider out of Platform, SmokeHost's <c>ProjectReference</c> to it began pointing at nothing,
/// and <c>dotnet build Nexus.Platform.slnx</c> could not report it because SmokeHost is not in the
/// solution. The same shape appeared a second time as a test project with zero tests
/// (<c>Nexus.Platform.Tests</c>, W5G finding D-13), which built successfully and could not fail.
/// </para>
///
/// <para>
/// <b>Why set equality and not a check with an exclusion list.</b> An exclusion list is a place to
/// put the next orphan. The rule here has no exceptions: every <c>*.csproj</c> in this repository is
/// named in the solution, and every project named in the solution exists. There are exactly two ways
/// to satisfy it — add the project, or remove it from the tree — and both are visible in a diff. The
/// only directories skipped are ones that are <b>not repository content</b>: <c>.git</c>, the
/// <c>.forge</c> worktree scaffolding, and build output (<c>bin</c>/<c>obj</c>).
/// </para>
///
/// <para>
/// <b>Non-vacuity is asserted, not assumed.</b> A walker that returns nothing satisfies "no
/// uncovered project" trivially, and a solution parser that returns nothing satisfies "no dangling
/// entry" trivially. Both are checked against a project that certainly exists — this one — so a
/// discovery mechanism that stops working fails the build rather than passing it.
/// </para>
/// </summary>
public sealed class ProjectCoverageTests
{
    /// <summary>Directories that hold no repository project. Everything else is walked.</summary>
    private static readonly string[] NonContentDirectories = [".git", ".forge", "bin", "obj", "node_modules"];

    [Fact]
    public void EveryProjectOnDisk_IsInTheSolution_AndEverySolutionEntry_ExistsOnDisk()
    {
        var root = RepositoryRoot();

        var onDisk = ProjectsOnDisk(root);
        var inSolution = ProjectsInSolution(root);

        // Non-vacuity, first: both discovery mechanisms must be able to see a project that
        // certainly exists. Without this, an empty enumeration would satisfy the set comparison
        // below and the guard would be a check that cannot fail.
        var self = "tests/Nexus.Platform.Architecture.Tests/Nexus.Platform.Architecture.Tests.csproj";
        Assert.Contains(self, onDisk);
        Assert.Contains(self, inSolution);

        var missingFromSolution = onDisk.Except(inSolution, PathComparer).OrderBy(p => p, PathComparer).ToList();
        var missingFromDisk = inSolution.Except(onDisk, PathComparer).OrderBy(p => p, PathComparer).ToList();

        Assert.True(
            missingFromSolution.Count == 0,
            "Project(s) exist on disk but are NOT in Nexus.Platform.slnx. Nothing builds them, so a "
            + "dangling ProjectReference inside them is invisible to the estate build. Add each to the "
            + "solution, or remove it from the tree. Missing: "
            + string.Join(", ", missingFromSolution));

        Assert.True(
            missingFromDisk.Count == 0,
            "Nexus.Platform.slnx names project(s) that do NOT exist at that path. The solution build "
            + "fails, or - worse - silently stops covering them. Fix the path, or remove the entry. "
            + "Dangling: " + string.Join(", ", missingFromDisk));
    }

    /// <summary>
    /// Every project on disk, as a forward-slashed path relative to the repository root.
    /// </summary>
    private static List<string> ProjectsOnDisk(string root)
    {
        var found = new List<string>();
        Walk(new DirectoryInfo(root), root, found);
        return found;
    }

    private static void Walk(DirectoryInfo directory, string root, List<string> found)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo child)
            {
                if (NonContentDirectories.Contains(child.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                Walk(child, root, found);
            }
            else if (entry.Name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(Relative(root, entry.FullName));
            }
        }
    }

    /// <summary>
    /// Every <c>&lt;Project Path="..." /&gt;</c> in the solution, in the same normalised form.
    /// </summary>
    private static List<string> ProjectsInSolution(string root)
    {
        var solutionPath = Path.Combine(root, "Nexus.Platform.slnx");

        var document = XDocument.Load(solutionPath);

        return document
            .Descendants()
            .Where(e => e.Name.LocalName == "Project")
            .Select(e => (string?)e.Attribute("Path"))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Replace('\\', '/'))
            .ToList();
    }

    private static string Relative(string root, string fullPath)
        => Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Locates the repository root by walking up for the solution file — the same mechanism
    /// <see cref="SecretScanGateTests"/> uses, and for the same reason: a hard-coded absolute path
    /// would drift, and this must work in CI, in a worktree, and on a developer's machine alike.
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
            + AppContext.BaseDirectory + ". The project-coverage check cannot run, and a guard that "
            + "cannot run must not report success.");
    }
}
