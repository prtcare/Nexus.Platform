using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Project;

namespace NexusAI.Application.Projects.Queries.GetProject;

public sealed record GetProjectResult(
    ProjectId ProjectId,
    WorkspaceId WorkspaceId,
    string Name,
    DateTimeOffset CreatedAt);