using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Workspace;

namespace NexusAI.Infrastructure.Dataverse.Repositories;

public sealed class WorkspaceDataverseRepository : IWorkspaceRepository
{
    public WorkspaceDataverseRepository()
    {
    }

    public Task AddAsync(
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Workspace?> GetAsync(
        WorkspaceId id,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task UpdateAsync(
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}