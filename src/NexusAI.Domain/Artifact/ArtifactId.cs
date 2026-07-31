namespace NexusAI.Domain.Artifact;

public readonly record struct ArtifactId(Guid Value)
{
    public static ArtifactId New() => new(Guid.NewGuid());
}