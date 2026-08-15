namespace NexusAI.Api.Endpoints.Projects;

public sealed record GetProjectResponse(
    Guid ProjectId,
    Guid WorkspaceId,
    string Name,
    DateTimeOffset CreatedAt);