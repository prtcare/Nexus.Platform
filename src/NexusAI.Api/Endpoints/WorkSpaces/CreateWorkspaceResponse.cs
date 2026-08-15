namespace NexusAI.Api.Endpoints.Workspaces;

public sealed record CreateWorkspaceResponse(
    Guid WorkspaceId,
    string Name);