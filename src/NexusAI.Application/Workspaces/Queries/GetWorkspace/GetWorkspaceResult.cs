using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Workspace;

namespace NexusAI.Application.Workspaces.Queries.GetWorkspace;

public sealed record GetWorkspaceResult(
    WorkspaceId WorkspaceId,
    string Name,
    string Owner,
    string Description,
    WorkspaceStatus Status,
    DateTimeOffset CreatedAt);