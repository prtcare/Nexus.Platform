using NexusAI.Domain.Artifact;
using NexusAI.Domain.WorkItem;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class ArtifactMapper
    : IRepositoryMapper<Artifact, ArtifactEntity>
{
    public ArtifactEntity ToEntity(Artifact domain)
    {
        return new ArtifactEntity
        {
            Id = domain.Id.Value,
            WorkItemId = domain.WorkItemId.Value,
            Name = domain.Name,
            Type = (int)domain.Type,
            Content = domain.Content,
            CreatedAt = domain.CreatedAt
        };
    }

    public Artifact ToDomain(ArtifactEntity entity)
    {
        return new Artifact(
            new ArtifactId(entity.Id),
            new WorkItemId(entity.WorkItemId),
            entity.Name,
            (ArtifactType)entity.Type,
            entity.Content,
            entity.CreatedAt);
    }
}