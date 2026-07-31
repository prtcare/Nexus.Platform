using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Workspace;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;
using NexusAI.Infrastructure.Dataverse.Mapping;

namespace NexusAI.Infrastructure.Dataverse.Repositories;

public sealed class WorkspaceDataverseRepository
    : DataverseRepositoryBase<Workspace, WorkspaceEntity, WorkspaceId>,
      IWorkspaceRepository
{
    public WorkspaceDataverseRepository(
        IDataverseClient client,
        IRepositoryMapper<Workspace, WorkspaceEntity> mapper)
        : base(client, mapper)
    {
    }

    public override Task AddAsync(
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(workspace, cancellationToken);
    }

    public override async Task<Workspace?> GetAsync(
        WorkspaceId id,
        CancellationToken cancellationToken = default)
    {
        var entity = await RetrieveAsync(
            id.Value,
            cancellationToken);

        if (entity is null)
        {
            return null;
        }

        return Mapper.ToDomain(entity);
    }

    public override Task UpdateAsync(
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        return UpdateEntityAsync(workspace, cancellationToken);
    }
}