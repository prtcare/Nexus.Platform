using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Workspace;

namespace NexusAI.Application.Workspaces.Queries.ListWorkspaces;

public sealed record ListWorkspacesResult(
    IReadOnlyList<WorkspaceSummary> Workspaces);

public sealed record WorkspaceSummary(
    WorkspaceId WorkspaceId,
    string Name,
    string Owner,
    string Description,
    WorkspaceStatus Status,
    DateTimeOffset CreatedAt);