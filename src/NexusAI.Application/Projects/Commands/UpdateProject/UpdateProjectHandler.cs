using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Project;

namespace NexusAI.Application.Projects.Commands.UpdateProject;

public sealed class UpdateProjectHandler
{
    private readonly IProjectRepository _repository;

    public UpdateProjectHandler(
        IProjectRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateProjectResult?> HandleAsync(
        UpdateProjectCommand command)
    {
        var project = await _repository.GetAsync(
            command.ProjectId,
            command.CancellationToken);

        if (project is null)
        {
            return null;
        }

        project.Rename(command.Name);

        await _repository.UpdateAsync(
            project,
            command.CancellationToken);

        return new UpdateProjectResult(
            project.Id,
            project.Name);
    }
}