using NexusAI.Domain.Common;
using NexusAI.Domain.WorkItem;

namespace NexusAI.Domain.Artifact;

public interface IArtifactRepository
    : IRepository<Artifact, ArtifactId>
{
    Task<IReadOnlyList<Artifact>> ListByWorkItemAsync(
        WorkItemId workItemId,
        CancellationToken cancellationToken = default);
}