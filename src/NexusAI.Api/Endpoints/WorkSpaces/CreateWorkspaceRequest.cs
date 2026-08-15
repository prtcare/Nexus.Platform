namespace NexusAI.Api.Endpoints.Workspaces;

public sealed record CreateWorkspaceRequest(
    string Name,
    string Owner,
    string Description);