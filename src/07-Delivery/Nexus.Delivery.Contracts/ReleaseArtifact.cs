namespace Nexus.Delivery.Contracts;

/// <summary>
/// One unit's immutable artifact inside a release bundle.
///
/// <para>
/// <b>On the name.</b> This type is deliberately <c>ReleaseArtifact</c> and not <c>Artifact</c>.
/// Platform's boundary suite (<c>PlatformBoundaryTests.Platform_MustNotContain_ProductTypeNames</c>)
/// forbids the bare type name <c>Artifact</c> in a Platform assembly, because <c>Artifact</c> is a
/// Product-Core domain concept (a document artifact inside Developer/Experience) and Platform must
/// not be able to express Product-Core structure. A deployment artifact is a different concept with
/// a coincidentally overlapping word, so the type is named for what it actually is: the artifact of
/// a release. This is a naming obligation, not an evasion — the forbidden-name check matches
/// <c>t.Name</c> exactly, and the intent of that check is served by not introducing the concept,
/// which this type does not.
/// </para>
///
/// <para>
/// There is no mutable state here and no setter. Once a bundle is registered, this record is the
/// identity of the bytes, and invariant I-1 says a digest never changes after registration: any
/// change is a NEW bundle, never an edit.
/// </para>
/// </summary>
public sealed record ReleaseArtifact
{
    public ReleaseArtifact(DeploymentUnitId unitId, ArtifactDigest digest, long sizeBytes)
    {
        ArgumentNullException.ThrowIfNull(unitId);
        ArgumentNullException.ThrowIfNull(digest);

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "An artifact size cannot be negative.");
        }

        UnitId = unitId;
        Digest = digest;
        SizeBytes = sizeBytes;
    }

    public DeploymentUnitId UnitId { get; }

    public ArtifactDigest Digest { get; }

    public long SizeBytes { get; }
}
