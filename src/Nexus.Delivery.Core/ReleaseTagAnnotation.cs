using System.Globalization;
using System.Text;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The governed identity block an annotated release tag carries in its message.
///
/// <para>
/// <b>This is what makes a release tag governed rather than merely named.</b> Anyone can create
/// <c>release/marketsurvey.api/0.1.0</c>; only the governed publisher writes this block into it. A later
/// assessor reads the block back and compares it to the release it was asked about, so a tag that merely
/// occupies the right name is identifiable as such — which is the difference between a naming convention
/// and a control.
/// </para>
///
/// <para>
/// <b>Formatted and parsed in one place.</b> A writer and a reader that each knew the format would drift,
/// and the drift would be invisible until a release was refused for a reason nobody could reproduce. The
/// estate has already paid for that defect class once, in the W9.2 secret scanner.
/// </para>
///
/// <para>
/// <b>The block carries no secret and no environment.</b> It names the release, its build, its artifacts
/// and their digests. Nothing else — a tag message is a published object and the least appropriate place
/// for a value.
/// </para>
/// </summary>
public sealed record ReleaseTagAnnotation
{
    /// <summary>First line of the block. Bumped only if the block's shape changes incompatibly.</summary>
    public const string Header = "nexus-release-reference 1";

    private ReleaseTagAnnotation(
        ReleaseId releaseId,
        DeploymentUnitId unitId,
        string version,
        BuildId buildId,
        ArtifactDigest recordDigest,
        string refName,
        IReadOnlyList<ReleaseArtifactIdentity> artifacts,
        IReadOnlyList<string> sourceCommits)
    {
        ReleaseId = releaseId;
        UnitId = unitId;
        Version = version;
        BuildId = buildId;
        RecordDigest = recordDigest;
        RefName = refName;
        Artifacts = artifacts;
        SourceCommits = sourceCommits;
    }

    public ReleaseId ReleaseId { get; }

    public DeploymentUnitId UnitId { get; }

    public string Version { get; }

    public BuildId BuildId { get; }

    /// <summary>The digest of the release record the tag was created for. This is what a moved tag cannot preserve.</summary>
    public ArtifactDigest RecordDigest { get; }

    public string RefName { get; }

    public IReadOnlyList<ReleaseArtifactIdentity> Artifacts { get; }

    public IReadOnlyList<string> SourceCommits { get; }

    /// <summary>Renders the block. Every collection is ordered, so identical releases render identical bytes.</summary>
    public static string Format(ReleaseRecord release)
    {
        ArgumentNullException.ThrowIfNull(release);

        var builder = new StringBuilder();
        builder.Append(Header).Append('\n');
        builder.Append("releaseId=").Append(release.ReleaseId.Value).Append('\n');
        builder.Append("unitId=").Append(release.UnitId.Value).Append('\n');
        builder.Append("version=").Append(release.Version).Append('\n');
        builder.Append("buildId=").Append(release.BuildId.Value).Append('\n');
        builder.Append("refName=").Append(release.Identity.ReleaseRefName).Append('\n');
        builder.Append("recordDigest=").Append(release.ComputeRecordDigest()).Append('\n');

        foreach (var artifact in release.Artifacts.OrderBy(a => a.ArtifactId.Value, StringComparer.Ordinal))
        {
            builder.Append("artifact=").Append(artifact.ArtifactId.Value).Append(' ')
                   .Append(artifact.ContentDigest).Append(' ')
                   .Append(artifact.SizeBytes.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        foreach (var commit in release.Identity.SourceCommits)
        {
            builder.Append("source=").Append(commit).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses a block. Returns false on anything malformed rather than throwing, so an assessor can report
    /// an unreadable annotation as a typed refusal instead of failing its caller.
    /// </summary>
    public static bool TryParse(string? message, out ReleaseTagAnnotation? annotation)
    {
        annotation = null;

        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var lines = message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || !string.Equals(lines[0].Trim(), Header, StringComparison.Ordinal))
        {
            return false;
        }

        string? releaseId = null, unitId = null, version = null, buildId = null, refName = null, recordDigest = null;
        var artifacts = new List<ReleaseArtifactIdentity>();
        var commits = new List<string>();

        foreach (var raw in lines.Skip(1))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                return false;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..];

            switch (key)
            {
                case "releaseId": releaseId = value; break;
                case "unitId": unitId = value; break;
                case "version": version = value; break;
                case "buildId": buildId = value; break;
                case "refName": refName = value; break;
                case "recordDigest": recordDigest = value; break;

                case "artifact":
                {
                    var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 3)
                    {
                        return false;
                    }

                    if (!long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var size))
                    {
                        return false;
                    }

                    artifacts.Add(new ReleaseArtifactIdentity(
                        Contracts.ArtifactId.Parse(parts[0]),
                        Contracts.ArtifactDigest.Parse(parts[1]),
                        size));
                    break;
                }

                case "source":
                    commits.Add(value);
                    break;

                default:
                    // An unknown key is not ignored. A block this reader does not fully understand is a
                    // block it cannot claim to have verified, and silently skipping the part it does not
                    // know is how a future field becomes invisible to every check.
                    return false;
            }
        }

        if (releaseId is null || unitId is null || version is null || buildId is null || refName is null
            || recordDigest is null || artifacts.Count == 0 || commits.Count == 0)
        {
            return false;
        }

        try
        {
            annotation = new ReleaseTagAnnotation(
                Contracts.ReleaseId.Parse(releaseId),
                Contracts.DeploymentUnitId.Parse(unitId),
                version,
                Contracts.BuildId.Parse(buildId),
                Contracts.ArtifactDigest.Parse(recordDigest),
                refName,
                artifacts,
                commits);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// True when this annotation describes the release it was read beside.
    ///
    /// <para>
    /// The record digest is compared as well as the release id, and that comparison is what detects a moved
    /// tag. The release id is derived from the identity alone, so a tag recreated at a different source
    /// commit with a corrected annotation would still carry the same release id if the identity happened not
    /// to include that commit. The record digest covers the whole record, so a repointed tag cannot satisfy
    /// it.
    /// </para>
    /// </summary>
    public bool Describes(ReleaseRecord release)
    {
        ArgumentNullException.ThrowIfNull(release);

        return ReleaseId == release.ReleaseId
               && UnitId == release.UnitId
               && string.Equals(Version, release.Version, StringComparison.Ordinal)
               && BuildId == release.BuildId
               && RecordDigest == release.ComputeRecordDigest();
    }
}
