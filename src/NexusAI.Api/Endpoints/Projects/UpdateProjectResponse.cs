namespace NexusAI.Api.Endpoints.Projects;

public sealed record UpdateProjectResponse(
    Guid ProjectId,
    string Name);