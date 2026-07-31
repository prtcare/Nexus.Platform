using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Knowledge;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class KnowledgeMapper
    : IRepositoryMapper<Knowledge, KnowledgeEntity>
{
    public KnowledgeEntity ToEntity(Knowledge domain)
    {
        return new KnowledgeEntity
        {
            Id = domain.Id.Value,
            WorkspaceId = domain.WorkspaceId.Value,
            Title = domain.Title,
            Content = domain.Content,
            Source = (int)domain.Source,
            CreatedAt = domain.CreatedAt
        };
    }

    public Knowledge ToDomain(KnowledgeEntity entity)
    {
        return new Knowledge(
            new KnowledgeId(entity.Id),
            new WorkspaceId(entity.WorkspaceId),
            entity.Title,
            entity.Content,
            (KnowledgeSource)entity.Source,
            entity.CreatedAt);
    }
}