using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Application.Workspaces.Commands.UpdateWorkspace;

public sealed record UpdateWorkspaceCommand(
    WorkspaceId WorkspaceId,
    string Name,
    string Owner,
    string Description);