using NexusAI.Domain.Project;

namespace NexusAI.Application.Projects.Queries.GetProject;

public sealed class GetProjectHandler
{
    private readonly IProjectRepository _repository;

    public GetProjectHandler(
        IProjectRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetProjectResult?> HandleAsync(
        GetProjectQuery query)
    {
        var project = await _repository.GetByIdAsync(
            query.ProjectId,
            query.CancellationToken);

        if (project is null)
        {
            return null;
        }

        return new GetProjectResult(
            project.Id,
            project.WorkspaceId,
            project.Name,
            project.CreatedAt);
    }
}