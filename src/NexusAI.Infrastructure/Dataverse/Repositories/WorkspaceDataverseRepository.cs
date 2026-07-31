using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Workspace;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;
using NexusAI.Infrastructure.Dataverse.Mapping;

namespace NexusAI.Infrastructure.Dataverse.Repositories;

public sealed class WorkspaceDataverseRepository
    : DataverseRepositoryBase<
        NexusAI.Domain.Workspace.Workspace,
        WorkspaceEntity,
        WorkspaceId>,
      IWorkspaceRepository
{
    public WorkspaceDataverseRepository(
        IDataverseClient client,
        IRepositoryMapper<
            NexusAI.Domain.Workspace.Workspace,
            WorkspaceEntity> mapper)
        : base(client, mapper)
    {
    }

    public override Task AddAsync(
        NexusAI.Domain.Workspace.Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(workspace, cancellationToken);
    }

    public override Task<NexusAI.Domain.Workspace.Workspace?> GetAsync(
        WorkspaceId id,
        CancellationToken cancellationToken = default)
    {
        return RetrieveDomainAsync(id.Value, cancellationToken);
    }

    public override Task UpdateAsync(
        NexusAI.Domain.Workspace.Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        return UpdateEntityAsync(workspace, cancellationToken);
    }
}