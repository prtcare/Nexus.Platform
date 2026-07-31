using NexusAI.Core.Abstractions;
using NexusAI.Domain.Project;

namespace NexusAI.Application.Projects.Commands.CreateProject;

public sealed class CreateProjectHandler
{
    private readonly IProjectRepository _repository;
    private readonly IClock _clock;

    public CreateProjectHandler(
        IProjectRepository repository,
        IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<CreateProjectResult> HandleAsync(
        CreateProjectCommand command)
    {
        var project = new Project(
            ProjectId.New(),
            command.WorkspaceId,
            command.Name,
            _clock.UtcNow);

        await _repository.AddAsync(
            project,
            command.CancellationToken);

        return new CreateProjectResult(
            project.Id,
            project.Name);
    }
}