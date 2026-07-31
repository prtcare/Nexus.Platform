using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Workspace;
using NexusAI.Infrastructure.Dataverse.Clients;

namespace NexusAI.Infrastructure.Dataverse.Repositories;

public sealed class WorkspaceDataverseRepository : IWorkspaceRepository
{
    private readonly IDataverseClient _client;

    public WorkspaceDataverseRepository(IDataverseClient client)
    {
        _client = client;
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