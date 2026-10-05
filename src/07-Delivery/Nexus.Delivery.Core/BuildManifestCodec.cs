using System.Text;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The canonical wire form of a <see cref="BuildManifest"/>.
///
/// <para>
/// <b>A line-based text format, deliberately.</b> The manifest's digest is computed from these bytes, so
/// the bytes must be identical for identical builds on any machine and in any process. A serializer's
/// output is a function of property discovery order, dictionary enumeration order and culture — all
/// stable in practice, none guaranteed — and a digest that depends on any of them would silently stop
/// proving identity. Here every field is written in a fixed order, every collection is sorted, and every
/// value is checked to contain no tab or newline, so the format cannot be ambiguous.
/// </para>
///
/// <para>
/// It is also readable by a person, which matters for the one property a reviewer most needs to confirm
/// at a glance: <b>no secret value appears anywhere in it</b>. Secrets are present only as reference
/// names, and configuration only as key names.
/// </para>
/// </summary>
public static class BuildManifestCodec
{
    private const string HeaderToken = "nexus-build-manifest";
    private const char Separator = '\t';
    private const char ListSeparator = '\u001f';

    /// <summary>Encodes a manifest to its canonical form. Deterministic for equal manifests.</summary>
    public static string Encode(BuildManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var builder = new StringBuilder();
        void Line(string key, params string[] values)
        {
            builder.Append(key);
            foreach (var value in values)
            {
                builder.Append(Separator).Append(Escape(value));
            }

            builder.Append('\n');
        }

        Line(HeaderToken, manifest.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Line("buildId", manifest.Identity.BuildId.Value);
        Line("inputDigest", manifest.Identity.InputDigest.ToString());
        Line("unitId", manifest.Identity.UnitId.Value);
        Line("buildConfiguration", manifest.Identity.BuildConfiguration);
        Line("buildDefinitionVersion", manifest.Identity.BuildDefinitionVersion);
        Line("buildTimestamp", manifest.BuiltAt.ToString("O"));

        foreach (var source in manifest.Identity.Sources)
        {
            Line("source", source.RepositoryLabel, source.CommitSha, source.WorkingTreeIsDirty ? "dirty" : "clean");
        }

        Line("toolchain", manifest.Identity.Toolchain.SdkVersion, manifest.Identity.Toolchain.RuntimeVersion,
            manifest.Identity.Toolchain.HostOperatingSystem, manifest.Identity.Toolchain.ContainerImage ?? string.Empty,
            manifest.Identity.Toolchain.NodeVersion ?? string.Empty);

        Line("dependencyLock", manifest.Identity.DependencyLock.IsLocked ? "locked" : "unlocked",
            manifest.Identity.DependencyLock.Note ?? string.Empty);

        foreach (var lockFile in manifest.Identity.DependencyLock.LockFiles)
        {
            Line("lockFile", lockFile.RepositoryLabel, lockFile.RelativePath, lockFile.Digest.ToString());
        }

        foreach (var artifact in manifest.Artifacts)
        {
            Line("artifact", artifact.ArtifactId.Value, artifact.ContentDigest.ToString(),
                artifact.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), artifact.FileName);
        }

        Line("test", manifest.Tests.SuiteName, manifest.Tests.Verdict.ToString(),
            manifest.Tests.Total.ToString(System.Globalization.CultureInfo.InvariantCulture),
            manifest.Tests.Passed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            manifest.Tests.Failed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            manifest.Tests.Skipped.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Line("secretScan", manifest.SecretScan.Verdict.ToString(),
            manifest.SecretScan.FilesScanned.ToString(System.Globalization.CultureInfo.InvariantCulture),
            manifest.SecretScan.FilesSkipped.ToString(System.Globalization.CultureInfo.InvariantCulture));

        foreach (var label in manifest.SecretScan.ScannedSubjectLabels)
        {
            Line("scanSubject", label);
        }

        foreach (var label in manifest.SecretScan.QuarantinedSubjectLabels)
        {
            Line("scanQuarantined", label);
        }

        foreach (var location in manifest.SecretScan.FindingLocations)
        {
            Line("scanFinding", location);
        }

        foreach (var location in manifest.SecretScan.QuarantinedFindingLocations)
        {
            Line("scanQuarantinedFinding", location);
        }

        foreach (var reason in manifest.SecretScan.IncompleteReasons)
        {
            Line("scanIncomplete", reason);
        }

        Line("reproducibility", manifest.Reproducibility.Verdict.ToString(),
            manifest.Reproducibility.BuildsCompared.ToString(System.Globalization.CultureInfo.InvariantCulture),
            manifest.Reproducibility.Note ?? string.Empty);

        foreach (var digest in manifest.Reproducibility.ComparisonDigests)
        {
            Line("reproDigest", digest.ToString());
        }

        Line("provenance", manifest.Provenance.BuilderRunId, manifest.Provenance.BuilderImageId,
            manifest.Provenance.HostOperatingSystem, manifest.Provenance.BuiltBy ?? string.Empty);

        foreach (var key in manifest.RuntimeConfigurationKeys)
        {
            Line("runtimeConfigKey", key);
        }

        return builder.ToString();
    }

    /// <summary>The manifest's content digest — its identity as a record.</summary>
    public static ArtifactDigest ComputeManifestDigest(BuildManifest manifest)
        => ArtifactDigest.Compute(Encoding.UTF8.GetBytes(Encode(manifest)));

    /// <summary>
    /// Parses a canonical manifest. Returns false on anything malformed rather than throwing, so a reader
    /// that meets a corrupt record can report a refusal with a reason.
    /// </summary>
    public static bool TryDecode(string? canonical, out BuildManifest? manifest)
    {
        manifest = null;

        if (string.IsNullOrWhiteSpace(canonical))
        {
            return false;
        }

        var lines = canonical.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        try
        {
            var fields = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
            var header = 0;

            foreach (var raw in lines)
            {
                var parts = raw.Split(Separator);
                if (parts.Length == 0)
                {
                    continue;
                }

                if (parts[0] == HeaderToken)
                {
                    header = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    continue;
                }

                if (!fields.TryGetValue(parts[0], out var list))
                {
                    fields[parts[0]] = list = [];
                }

                list.Add([.. parts[1..].Select(Unescape)]);
            }

            string Single(string key, int index = 0)
                => fields.TryGetValue(key, out var v) && v.Count > index && v[index].Length > index
                    ? v[index][index]
                    : throw new FormatException($"Missing field '{key}'.");

            var sources = (fields.TryGetValue("source", out var sourceRows) ? sourceRows : [])
                .Select(row => new SourceRevision(row[0], row[1], row[2] == "dirty"))
                .ToArray();

            var toolchainRow = fields["toolchain"][0];
            var toolchain = new ToolchainIdentity(
                toolchainRow[0],
                toolchainRow[1],
                toolchainRow[2],
                string.IsNullOrEmpty(toolchainRow[3]) ? null : toolchainRow[3],
                string.IsNullOrEmpty(toolchainRow[4]) ? null : toolchainRow[4]);

            var lockRow = fields["dependencyLock"][0];
            var lockFiles = (fields.TryGetValue("lockFile", out var lockRows) ? lockRows : [])
                .Select(row => new LockFileDigest(row[0], row[1], ArtifactDigest.Parse(row[2])))
                .ToArray();

            var dependencyLock = lockRow[0] == "locked"
                ? DependencyLockState.Locked(lockFiles)
                : DependencyLockState.Unlocked(lockRow[1]);

            var identity = new BuildIdentity(
                DeploymentUnitId.Parse(Single("unitId")),
                sources,
                Single("buildDefinitionVersion"),
                dependencyLock,
                toolchain,
                Single("buildConfiguration"));

            var artifacts = (fields.TryGetValue("artifact", out var artifactRows) ? artifactRows : [])
                .Select(row => new PackagedArtifact(
                    ArtifactIdFromValue(row[0]),
                    ArtifactDigest.Parse(row[1]),
                    long.Parse(row[2], System.Globalization.CultureInfo.InvariantCulture),
                    identity.BuildId,
                    row[3]))
                .ToArray();

            var testRow = fields["test"][0];
            var tests = new BuildTestEvidence(
                testRow[0],
                Enum.Parse<TestVerdict>(testRow[1]),
                int.Parse(testRow[2], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(testRow[3], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(testRow[4], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(testRow[5], System.Globalization.CultureInfo.InvariantCulture));

            var scanRow = fields["secretScan"][0];
            var secretScan = new SecretScanEvidence(
                Enum.Parse<SecretScanVerdict>(scanRow[0]),
                Rows("scanSubject"),
                Rows("scanQuarantined"),
                int.Parse(scanRow[1], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(scanRow[2], System.Globalization.CultureInfo.InvariantCulture),
                Rows("scanFinding"),
                Rows("scanIncomplete"),
                Rows("scanQuarantinedFinding"));

            var reproRow = fields["reproducibility"][0];
            var reproducibility = new ReproducibilityEvidence(
                Enum.Parse<ReproducibilityVerdict>(reproRow[0]),
                int.Parse(reproRow[1], System.Globalization.CultureInfo.InvariantCulture),
                [.. Rows("reproDigest").Select(ArtifactDigest.Parse)],
                string.IsNullOrEmpty(reproRow[2]) ? null : reproRow[2]);

            var provenanceRow = fields["provenance"][0];
            var provenance = new ProvenanceRecord(
                provenanceRow[0],
                provenanceRow[1],
                provenanceRow[2],
                string.IsNullOrEmpty(provenanceRow[3]) ? null : provenanceRow[3]);

            manifest = new BuildManifest(
                header,
                identity,
                artifacts,
                tests,
                secretScan,
                reproducibility,
                provenance,
                DateTimeOffset.Parse(Single("buildTimestamp"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                Rows("runtimeConfigKey"));

            return true;

            List<string> Rows(string key) => fields.TryGetValue(key, out var rows)
                ? [.. rows.Select(r => r[0])]
                : [];
        }
        catch (Exception ex) when (ex is FormatException or KeyNotFoundException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>Reconstructs an artifact id from its canonical value, for decoding. The segment mapping is beside <see cref="ArtifactId"/>.</summary>
    private static ArtifactId ArtifactIdFromValue(string value)
    {
        var firstSlash = value.IndexOf('/', StringComparison.Ordinal);
        var secondSlash = value.IndexOf('/', firstSlash + 1);
        var at = value.LastIndexOf('@');

        if (firstSlash <= 0 || secondSlash <= 0 || at <= secondSlash)
        {
            throw new FormatException($"Malformed artifact id '{value}'.");
        }

        var type = value[(firstSlash + 1)..secondSlash] switch
        {
            "dotnet-app" => ArtifactType.DotnetApplication,
            "dotnet-lib" => ArtifactType.DotnetLibrary,
            "client-bundle" => ArtifactType.StaticClientBundle,
            "package" => ArtifactType.Package,
            "container-image" => ArtifactType.ContainerImage,
            var other => throw new FormatException($"Unknown artifact type segment '{other}'.")
        };

        return ArtifactId.For(
            DeploymentUnitId.Parse(value[..firstSlash]),
            type,
            value[(secondSlash + 1)..at],
            value[(at + 1)..]);
    }

    /// <summary>
    /// Escapes the two characters the format uses structurally. A value containing a tab or a newline
    /// would otherwise be able to fabricate a field boundary, which is the one way this format could be
    /// made ambiguous.
    /// </summary>
    private static string Escape(string? value)
        => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");

    private static string Unescape(string value)
    {
        var builder = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i == value.Length - 1)
            {
                builder.Append(value[i]);
                continue;
            }

            i++;
            builder.Append(value[i] switch
            {
                't' => '\t',
                'n' => '\n',
                'r' => '\r',
                '\\' => '\\',
                var other => other
            });
        }

        return builder.ToString();
    }
}
