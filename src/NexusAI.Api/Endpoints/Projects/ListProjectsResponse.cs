namespace NexusAI.Api.Endpoints.Projects;

public sealed record ListProjectsResponse(
    Guid ProjectId,
    string Name,
    DateTimeOffset CreatedAt);