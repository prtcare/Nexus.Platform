using NexusAI.Domain.Adr;
using NexusAI.Domain.Knowledge;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class AdrMapper
    : IRepositoryMapper<Adr, AdrEntity>
{
    public AdrEntity ToEntity(Adr domain)
    {
        return new AdrEntity
        {
            Id = domain.Id.Value,
            KnowledgeId = domain.KnowledgeId.Value,
            Title = domain.Title,
            Decision = domain.Decision,
            Status = (int)domain.Status,
            CreatedAt = domain.CreatedAt
        };
    }

    public Adr ToDomain(AdrEntity entity)
    {
        return new Adr(
            new AdrId(entity.Id),
            new KnowledgeId(entity.KnowledgeId),
            entity.Title,
            entity.Decision,
            (AdrStatus)entity.Status,
            entity.CreatedAt);
    }
}