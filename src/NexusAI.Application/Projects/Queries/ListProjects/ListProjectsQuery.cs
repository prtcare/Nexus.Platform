using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Application.Projects.Queries.ListProjects;

public sealed record ListProjectsQuery(
    WorkspaceId WorkspaceId,
    CancellationToken CancellationToken = default);