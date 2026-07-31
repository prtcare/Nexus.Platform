using NexusAI.Domain.Knowledge;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Repositories;

public sealed class KnowledgeDataverseRepository
    : DataverseRepositoryBase<
        Knowledge,
        KnowledgeEntity,
        KnowledgeId>,
      IKnowledgeRepository
{
    public KnowledgeDataverseRepository(
        IDataverseClient client,
        IRepositoryMapper<Knowledge, KnowledgeEntity> mapper)
        : base(client, mapper)
    {
    }

    public override Task AddAsync(
        Knowledge domain,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(domain, cancellationToken);
    }

    public override Task<Knowledge?> GetAsync(
        KnowledgeId id,
        CancellationToken cancellationToken = default)
    {
        return RetrieveDomainAsync(id.Value, cancellationToken);
    }

    public override Task UpdateAsync(
        Knowledge domain,
        CancellationToken cancellationToken = default)
    {
        return UpdateEntityAsync(domain, cancellationToken);
    }
}