using NexusAI.Domain.Common;
using NexusAI.Infrastructure.Dataverse.Common;

namespace NexusAI.Domain.Artifact;

public interface IArtifactRepository
    : IRepository<Artifact, ArtifactId>
{
}