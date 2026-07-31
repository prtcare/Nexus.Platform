using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Application.Projects.Commands.CreateProject;

public sealed record CreateProjectCommand(
    WorkspaceId WorkspaceId,
    string Name,
    CancellationToken CancellationToken = default);