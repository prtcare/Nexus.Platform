namespace NexusAI.Api.Endpoints.Workspaces;

public sealed record UpdateWorkspaceResponse(
    Guid WorkspaceId,
    string Name);