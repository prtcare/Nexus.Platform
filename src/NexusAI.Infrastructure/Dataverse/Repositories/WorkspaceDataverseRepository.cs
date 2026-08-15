using NexusAI.Domain.Common.Identifiers;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Repositories;

public sealed class WorkspaceDataverseRepository
    : DataverseRepositoryBase<
        NexusAI.Domain.Workspace.Workspace,
        WorkspaceEntity,
        WorkspaceId>,
      NexusAI.Domain.Workspace.IWorkspaceRepository
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
        return CreateAsync(
            workspace,
            cancellationToken);
    }

    public override Task<NexusAI.Domain.Workspace.Workspace?> GetAsync(
        WorkspaceId id,
        CancellationToken cancellationToken = default)
    {
        return RetrieveDomainAsync(
            id.Value,
            cancellationToken);
    }

    public override Task UpdateAsync(
        NexusAI.Domain.Workspace.Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        return UpdateEntityAsync(
            workspace,
            cancellationToken);
    }

    public Task<IReadOnlyList<NexusAI.Domain.Workspace.Workspace>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return RetrieveMultipleDomainAsync(
            _ => true,
            cancellationToken);
    }
}