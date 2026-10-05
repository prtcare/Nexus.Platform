namespace Nexus.Delivery.Contracts;

/// <summary>
/// One built artifact, as it exists after packaging and before it is published anywhere.
///
/// <para>
/// <b>There is no environment member, and there is no configuration member.</b> That is the shape of the
/// build-once rule expressed as a type: an artifact knows what it is, what it was built from, and what
/// its bytes hash to. It does not know where it will run. Anything an artifact needed to know about its
/// environment would have to be either baked in — which makes it environment-specific, i.e. a different
/// build — or supplied at run time, which is what the runtime-configuration pattern is for.
/// </para>
///
/// <para>
/// <see cref="FileName"/> is recorded because a fetch has to materialise something on disk, but it is
/// presentation, not identity: two artifacts with the same id and the same digest are the same artifact
/// whatever they are called locally.
/// </para>
/// </summary>
public sealed record PackagedArtifact
{
    public PackagedArtifact(
        ArtifactId artifactId,
        ArtifactDigest contentDigest,
        long sizeBytes,
        BuildId buildId,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(artifactId);
        ArgumentNullException.ThrowIfNull(contentDigest);
        ArgumentNullException.ThrowIfNull(buildId);

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "An artifact size cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("A packaged artifact must name the file it was written as.", nameof(fileName));
        }

        ArtifactId = artifactId;
        ContentDigest = contentDigest;
        SizeBytes = sizeBytes;
        BuildId = buildId;
        FileName = fileName;
    }

    public ArtifactId ArtifactId { get; }

    /// <summary>SHA-256 over the packaged bytes. This is what a promotion compares.</summary>
    public ArtifactDigest ContentDigest { get; }

    public long SizeBytes { get; }

    /// <summary>The build that produced it. Joins the artifact to its governed inputs.</summary>
    public BuildId BuildId { get; }

    public string FileName { get; }

    public DeploymentUnitId UnitId => ArtifactId.UnitId;

    public ArtifactType Type => ArtifactId.Type;

    /// <summary>The manifest form: no local path, no environment, nothing but identity and provenance.</summary>
    public ReleaseArtifact ToReleaseArtifact() => new(UnitId, ContentDigest, SizeBytes);

    public override string ToString() => $"{ArtifactId.Value} ({ContentDigest}, {SizeBytes} bytes)";
}
