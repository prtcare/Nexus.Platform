using NexusAI.Domain.Project;

namespace NexusAI.Application.Projects.Queries.ListProjects;

public sealed class ListProjectsHandler
{
    private readonly IProjectRepository _repository;

    public ListProjectsHandler(
        IProjectRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ListProjectsResult>> HandleAsync(
        ListProjectsQuery query)
    {
        var projects = await _repository.ListByWorkspaceAsync(
            query.WorkspaceId,
            query.CancellationToken);

        return projects
            .Select(x => new ListProjectsResult(
                x.Id.Value,
                x.Name,
                x.CreatedAt))
            .ToList();
    }
}