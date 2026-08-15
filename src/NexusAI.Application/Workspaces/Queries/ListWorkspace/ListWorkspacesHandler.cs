using NexusAI.Domain.Workspace;

namespace NexusAI.Application.Workspaces.Queries.ListWorkspaces;

public sealed class ListWorkspacesHandler
{
    private readonly IWorkspaceRepository _repository;

    public ListWorkspacesHandler(
        IWorkspaceRepository repository)
    {
        _repository = repository;
    }

    public async Task<ListWorkspacesResult> HandleAsync(
        ListWorkspacesQuery query,
        CancellationToken cancellationToken = default)
    {
        var workspaces = await _repository.ListAsync(
            cancellationToken);

        var results = workspaces
            .Select(workspace =>
                new WorkspaceSummary(
                    workspace.Id,
                    workspace.Name,
                    workspace.Owner,
                    workspace.Description,
                    workspace.Status,
                    workspace.CreatedAt))
            .ToList();

        return new ListWorkspacesResult(results);
    }
}