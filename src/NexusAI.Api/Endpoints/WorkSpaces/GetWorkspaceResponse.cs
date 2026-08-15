namespace NexusAI.Api.Endpoints.Workspaces;

public sealed record GetWorkspaceResponse(
    Guid WorkspaceId,
    string Name,
    string Owner,
    string Description,
    int Status,
    DateTimeOffset CreatedAt);