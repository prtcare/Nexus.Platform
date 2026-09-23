using System.IO.Compression;
using System.Text;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>The result of inspecting a packaged artifact's bytes for environment dependence.</summary>
public sealed record EnvironmentNeutralityResult(
    bool IsNeutral,
    int FilesInspected,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> SkippedFiles);

/// <summary>
/// Inspects a packaged artifact for evidence that an environment was compiled into it.
///
/// <para>
/// <b>Why this exists rather than a declaration.</b> Whether a unit "still requires an
/// environment-specific rebuild" is, by nature, invisible in the artifact: a baked endpoint looks exactly
/// like an ordinary value. W9.0 left it as something a plan asserts, and an assertion that cannot be checked
/// is the shape this estate keeps recording as a defect. So the plan's claim is now only able to ADD
/// suspicion — this inspection can FIND a dependence and force a refusal, and nothing the plan says can
/// clear a finding this produces.
/// </para>
///
/// <para>
/// <b>What it looks for, and why that is the right thing.</b> The ratified environment identifiers —
/// <c>ENV-DEV</c>, <c>ENV-TEST</c>, <c>ENV-PROD</c> — plus any additional markers a caller supplies. An
/// artifact that cannot see its environment cannot contain its name, so the presence of the name is direct
/// evidence of the dependence, and its absence is meaningful evidence against it. This is deliberately a
/// <i>narrow</i> test: it does not try to detect "configuration" in general, because it cannot, and a check
/// that guessed would be muted the first time it guessed wrong.
/// </para>
///
/// <para>
/// <b>What it does not prove.</b> Absence of an environment identifier is not proof of neutrality — an
/// artifact could depend on its environment through an endpoint, a feature flag or a host name that carries
/// no such identifier. The check is one direction only, and the report says so.
/// </para>
/// </summary>
public static class EnvironmentNeutralityInspector
{
    /// <summary>File extensions whose contents are inspected. Anything binary is skipped rather than guessed at.</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".js", ".mjs", ".cjs", ".ts", ".map", ".txt", ".xml", ".config", ".html", ".css",
        ".dll", ".pdb", ".runtimeconfig", ".deps", ".props", ".targets", ".nuspec", ".md"
    };

    /// <summary>
    /// Inspects a packaged artifact — a zip, as produced by <see cref="ZipDirectoryPackager"/>.
    /// </summary>
    /// <param name="artifactPath">The packaged artifact.</param>
    /// <param name="additionalMarkers">Extra strings that, if present, indicate an environment dependence.</param>
    public static EnvironmentNeutralityResult Inspect(string artifactPath, IReadOnlyList<string>? additionalMarkers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        var findings = new List<string>();
        var skipped = new List<string>();
        var inspected = 0;

        var markers = new List<string>
        {
            DeploymentEnvironmentId.Dev,
            DeploymentEnvironmentId.Test,
            DeploymentEnvironmentId.Prod
        };
        markers.AddRange(additionalMarkers ?? []);

        if (!File.Exists(artifactPath))
        {
            return new EnvironmentNeutralityResult(false, 0, ["The artifact to inspect does not exist."], []);
        }

        using var archive = ZipFile.OpenRead(artifactPath);

        foreach (var entry in archive.Entries)
        {
            if (entry.Length == 0)
            {
                continue;
            }

            var extension = Path.GetExtension(entry.Name);
            if (!TextExtensions.Contains(extension))
            {
                skipped.Add(entry.FullName);
                continue;
            }

            // Assemblies and executables are scanned as raw bytes rather than decoded: a string literal in
            // a .NET assembly is UTF-16 in the metadata heap, so a text decode would miss exactly what this
            // check is for. The scan is a byte search, so encoding does not matter.
            var bytes = ReadEntry(entry);
            inspected++;

            foreach (var marker in markers)
            {
                if (ContainsMarker(bytes, marker, out var offset))
                {
                    findings.Add(
                        $"{entry.FullName}: contains '{marker}' at byte offset {offset}. "
                        + "An artifact that can see its environment is one that must be rebuilt per environment.");
                }
            }
        }

        return new EnvironmentNeutralityResult(findings.Count == 0, inspected, findings, skipped);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// A byte search for the marker in either UTF-8 or UTF-16LE. Both encodings are tried because a .NET
    /// assembly stores literals as UTF-16 in its metadata heap while a JavaScript or JSON file stores them as
    /// UTF-8 — and a check that silently missed one of those would report neutrality it had not established.
    /// </summary>
    private static bool ContainsMarker(byte[] haystack, string marker, out int offset)
    {
        var utf8 = Encoding.UTF8.GetBytes(marker);
        if (IndexOf(haystack, utf8) is var utf8Index && utf8Index >= 0)
        {
            offset = utf8Index;
            return true;
        }

        var utf16 = Encoding.Unicode.GetBytes(marker);
        var utf16Index = IndexOf(haystack, utf16);
        if (utf16Index >= 0)
        {
            offset = utf16Index;
            return true;
        }

        offset = -1;
        return false;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return -1;
        }

        var limit = haystack.Length - needle.Length;
        for (var i = 0; i <= limit; i++)
        {
            var matched = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return i;
            }
        }

        return -1;
    }
}
