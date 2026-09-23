using System.Globalization;
using System.Text;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// One artifact's immutable coordinate inside a release: which artifact, which bytes, how many, what kind.
///
/// <para>
/// <b>Why this is not just a digest.</b> W9.1's <see cref="ReleaseArtifact"/> carries a unit and a digest,
/// which is enough to compare two environments but not enough to fetch anything: a digest does not name an
/// artifact, and the artifact store is addressed by <see cref="ArtifactId"/>. W9.0 recorded the consequence
/// as a missing capability — "no unit reports its own build id, digest or commit" — and the remedy is that a
/// release names the addressable artifact, not only its hash.
/// </para>
///
/// <para>
/// The digest remains a first-class member rather than being derived from the id, because the two answer
/// different questions. The id says <i>which release coordinate</i>; the digest says <i>which bytes</i>. A
/// release is only sound when both are pinned, and W9.2's store already refuses an id whose bytes changed.
/// </para>
/// </summary>
public sealed record ReleaseArtifactIdentity
{
    public ReleaseArtifactIdentity(ArtifactId artifactId, ArtifactDigest contentDigest, long sizeBytes)
    {
        ArgumentNullException.ThrowIfNull(artifactId);
        ArgumentNullException.ThrowIfNull(contentDigest);

        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sizeBytes),
                sizeBytes,
                "A released artifact must have a positive size. A zero-byte release input is a packaging failure, not a small artifact.");
        }

        ArtifactId = artifactId;
        ContentDigest = contentDigest;
        SizeBytes = sizeBytes;
    }

    public ArtifactId ArtifactId { get; }

    public ArtifactDigest ContentDigest { get; }

    public long SizeBytes { get; }

    public DeploymentUnitId UnitId => ArtifactId.UnitId;

    public ArtifactType Type => ArtifactId.Type;

    public string Version => ArtifactId.Version;

    /// <summary>The W9.1 bundle form, for the registry that speaks in units and digests.</summary>
    public ReleaseArtifact ToReleaseArtifact() => new(UnitId, ContentDigest, SizeBytes);

    /// <summary>Canonical, digest-stable rendering. NUL-terminated fields so no two identities can concatenate into the same string.</summary>
    internal void AppendCanonicalForm(StringBuilder builder)
    {
        builder.Append(ArtifactId.Value).Append('\0')
               .Append(ContentDigest).Append('\0')
               .Append(SizeBytes.ToString(CultureInfo.InvariantCulture)).Append('\0');
    }

    public override string ToString() => $"{ArtifactId.Value} ({ContentDigest}, {SizeBytes} bytes)";
}

/// <summary>
/// <b>The release identity: everything that makes a release <i>this</i> release, and nothing about where it
/// runs.</b>
///
/// <para>
/// The member list is the argument. There is a unit, a version, the certified build that produced the bytes,
/// the commits those bytes came from, the governed reference the release is published under, and the
/// immutable artifacts themselves. There is no environment, no endpoint, no host name, no instance count,
/// no configuration value. That absence is load-bearing rather than incidental: <b>the same ReleaseId is
/// promoted through environments</b>, so an environment-shaped field here would make every environment a
/// different release, which is exactly the rebuild-per-environment failure the build-once rule forbids.
/// </para>
///
/// <para>
/// A test asserts the absence by reflection rather than trusting this paragraph, because a future member
/// added in good faith is precisely how such a property is lost.
/// </para>
///
/// <para>
/// <b>Version comes from the certified artifact and is never invented here.</b> The artifact's own
/// <see cref="ArtifactId.Version"/> is the single source, and this type refuses a release whose stated
/// version disagrees with it. A stage that "bumped the version for the release" would break the one thing
/// the version is for: naming the same coordinate in the store, the bundle and the release reference.
/// </para>
/// </summary>
public sealed record ReleaseIdentity
{
    public ReleaseIdentity(
        DeploymentUnitId unitId,
        string version,
        BuildId buildId,
        IReadOnlyList<string> sourceCommits,
        string releaseRefName,
        IReadOnlyList<ReleaseArtifactIdentity> artifacts)
    {
        ArgumentNullException.ThrowIfNull(unitId);
        ArgumentNullException.ThrowIfNull(buildId);
        ArgumentNullException.ThrowIfNull(artifacts);

        if (!ArtifactId.IsValidVersion(version))
        {
            throw new ArgumentException(
                "A release version must be a valid artifact version (dotted digits, optional name-shaped suffix). It is the certified artifact's version, not a value chosen for the release.",
                nameof(version));
        }

        if (sourceCommits is null || sourceCommits.Count == 0)
        {
            throw new ArgumentException("A release identity must name at least one source commit.", nameof(sourceCommits));
        }

        foreach (var commit in sourceCommits)
        {
            if (string.IsNullOrWhiteSpace(commit) || !IsHex(commit))
            {
                throw new ArgumentException($"A source commit must be hexadecimal; '{commit}' is not.", nameof(sourceCommits));
            }
        }

        if (string.IsNullOrWhiteSpace(releaseRefName))
        {
            throw new ArgumentException("A release identity must name its release reference.", nameof(releaseRefName));
        }

        if (artifacts.Count == 0)
        {
            throw new ArgumentException("A release must carry at least one artifact. A release of nothing is not a release.", nameof(artifacts));
        }

        foreach (var artifact in artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
        }

        var duplicate = artifacts
            .GroupBy(a => a.ArtifactId.Value, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"An artifact id appears more than once in the release: '{duplicate.Key}'.",
                nameof(artifacts));
        }

        // The version is the artifact's, and saying so is enforced rather than documented: a release whose
        // version disagreed with the artifact it carries could not be resolved back to that artifact.
        var mismatched = artifacts.FirstOrDefault(a => !string.Equals(a.Version, version, StringComparison.Ordinal));
        if (mismatched is not null)
        {
            throw new ArgumentException(
                $"The release version '{version}' disagrees with artifact '{mismatched.ArtifactId.Value}' (version '{mismatched.Version}'). "
                + "A release version is derived from the certified artifact, never chosen.",
                nameof(version));
        }

        // One unit per release: the release reference is per unit, and a reference naming two units could
        // not say which one it releases.
        var units = artifacts.Select(a => a.UnitId.Value).Distinct(StringComparer.Ordinal).ToList();
        if (units.Count > 1)
        {
            throw new ArgumentException(
                $"A release carries one deployment unit; this one carries {units.Count}: {string.Join(", ", units)}.",
                nameof(artifacts));
        }

        UnitId = unitId;
        Version = version;
        BuildId = buildId;
        SourceCommits = [.. sourceCommits.Select(c => c.ToLowerInvariant()).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal)];
        ReleaseRefName = releaseRefName;
        Artifacts = [.. artifacts.OrderBy(a => a.ArtifactId.Value, StringComparer.Ordinal)];

        InputDigest = ComputeInputDigest();
        ReleaseId = ReleaseId.FromInputDigest(InputDigest);
    }

    public DeploymentUnitId UnitId { get; }

    /// <summary>The certified artifact's version. Never a value this stage chose.</summary>
    public string Version { get; }

    /// <summary>The certified build these bytes came from.</summary>
    public BuildId BuildId { get; }

    /// <summary>Ordered, de-duplicated, lowercase. The commits the certified build recorded.</summary>
    public IReadOnlyList<string> SourceCommits { get; }

    /// <summary>The immutable release reference, e.g. <c>release/marketsurvey.api/0.1.0</c>.</summary>
    public string ReleaseRefName { get; }

    public IReadOnlyList<ReleaseArtifactIdentity> Artifacts { get; }

    public ArtifactDigest InputDigest { get; }

    public ReleaseId ReleaseId { get; }

    /// <summary>
    /// The deterministic input to <see cref="ReleaseId"/>. Every field is NUL-terminated so that
    /// <c>["ab","c"]</c> and <c>["a","bc"]</c> cannot produce the same digest, and every collection is
    /// ordered so that iteration order cannot leak in.
    /// </summary>
    public string CanonicalForm()
    {
        var builder = new StringBuilder();

        void Field(string? value) => builder.Append(value ?? string.Empty).Append('\0');

        Field("nexus-release-identity");
        Field(UnitId.Value);
        Field(Version);
        Field(BuildId.Value);
        Field(ReleaseRefName);

        foreach (var commit in SourceCommits)
        {
            Field(commit);
        }

        foreach (var artifact in Artifacts)
        {
            artifact.AppendCanonicalForm(builder);
        }

        return builder.ToString();
    }

    private ArtifactDigest ComputeInputDigest()
        => ArtifactDigest.Compute(Encoding.UTF8.GetBytes(CanonicalForm()));

    private static bool IsHex(string value)
        => value.All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    public override string ToString() => $"{ReleaseId} {UnitId}@{Version} from {BuildId}";
}
