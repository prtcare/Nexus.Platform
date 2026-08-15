namespace NexusAI.Api.Endpoints.Workspaces;

public sealed record ListWorkspacesResponse(
    IReadOnlyList<GetWorkspaceResponse> Workspaces);