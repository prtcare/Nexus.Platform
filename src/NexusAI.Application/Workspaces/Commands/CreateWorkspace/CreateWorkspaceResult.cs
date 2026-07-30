using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Application.Workspaces.Commands.CreateWorkspace;

public sealed record CreateWorkspaceResult(
    WorkspaceId WorkspaceId,
    string Name);