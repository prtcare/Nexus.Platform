using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Application.Workspaces.Queries.GetWorkspace;

public sealed record GetWorkspaceQuery(
    WorkspaceId WorkspaceId);