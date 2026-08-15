using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Project;

namespace NexusAI.Application.Projects.Commands.UpdateProject;

public sealed record UpdateProjectCommand(
    ProjectId ProjectId,
    string Name,
    CancellationToken CancellationToken = default);