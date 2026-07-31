using NexusAI.Domain.WorkItem;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Repositories;

public sealed class WorkItemDataverseRepository
    : DataverseRepositoryBase<WorkItem, WorkItemEntity, WorkItemId>,
      IWorkItemRepository
{
    public WorkItemDataverseRepository(
        IDataverseClient client,
        IRepositoryMapper<WorkItem, WorkItemEntity> mapper)
        : base(client, mapper)
    {
    }

    public override Task AddAsync(
        WorkItem domain,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(domain, cancellationToken);
    }

    public override Task<WorkItem?> GetAsync(
        WorkItemId id,
        CancellationToken cancellationToken = default)
    {
        return RetrieveDomainAsync(id.Value, cancellationToken);
    }

    public override Task UpdateAsync(
        WorkItem domain,
        CancellationToken cancellationToken = default)
    {
        return UpdateEntityAsync(domain, cancellationToken);
    }
}