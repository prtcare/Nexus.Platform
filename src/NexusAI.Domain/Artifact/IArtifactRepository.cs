using NexusAI.Domain.Common;


namespace NexusAI.Domain.Artifact;

public interface IArtifactRepository
    : IRepository<Artifact, ArtifactId>
{
}