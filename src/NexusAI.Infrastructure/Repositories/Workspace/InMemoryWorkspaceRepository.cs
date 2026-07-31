using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Workspace;
using WorkspaceEntity = NexusAI.Domain.Workspace.Workspace;

namespace NexusAI.Infrastructure.Repositories.Workspace;

public sealed class InMemoryWorkspaceRepository
    : IWorkspaceRepository
{
    private readonly Dictionary<WorkspaceId, WorkspaceEntity> _workspaces = new();

    public Task AddAsync(
        WorkspaceEntity workspace,
        CancellationToken cancellationToken = default)
    {
        _workspaces[workspace.Id] = workspace;
        return Task.CompletedTask;
    }

    public Task<WorkspaceEntity?> GetAsync(
        WorkspaceId id,
        CancellationToken cancellationToken = default)
    {
        _workspaces.TryGetValue(id, out var workspace);

        return Task.FromResult(workspace);
    }

    public Task UpdateAsync(
        WorkspaceEntity workspace,
        CancellationToken cancellationToken = default)
    {
        _workspaces[workspace.Id] = workspace;
        return Task.CompletedTask;
    }
}