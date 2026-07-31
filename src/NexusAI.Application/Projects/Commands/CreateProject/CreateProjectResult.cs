using NexusAI.Domain.Project;

namespace NexusAI.Application.Projects.Commands.CreateProject;

public sealed record CreateProjectResult(
    ProjectId ProjectId,
    string Name);