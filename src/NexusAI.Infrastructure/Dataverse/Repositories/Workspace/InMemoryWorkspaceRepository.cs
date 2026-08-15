using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Workspace;
using WorkspaceEntity = NexusAI.Domain.Workspace.Workspace;

namespace NexusAI.Infrastructure.Dataverse.Repositories.Workspace;

public sealed class InMemoryWorkspaceRepository
    : IWorkspaceRepository
{
    private readonly Dictionary<Guid, NexusAI.Domain.Workspace.Workspace> _workspaces = new();

    public Task AddAsync(
    NexusAI.Domain.Workspace.Workspace workspace,
    CancellationToken cancellationToken = default)
    {
        _workspaces[workspace.Id.Value] = workspace;

        return Task.CompletedTask;
    }

    public Task<NexusAI.Domain.Workspace.Workspace?> GetAsync(
        WorkspaceId id,
        CancellationToken cancellationToken = default)
    {
        _workspaces.TryGetValue(
            id.Value,
            out var workspace);

        return Task.FromResult(workspace);
    }

    public Task UpdateAsync(
        NexusAI.Domain.Workspace.Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        _workspaces[workspace.Id.Value] = workspace;

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NexusAI.Domain.Workspace.Workspace>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NexusAI.Domain.Workspace.Workspace> result =
            _workspaces.Values.ToList();

        return Task.FromResult(result);
    }
}