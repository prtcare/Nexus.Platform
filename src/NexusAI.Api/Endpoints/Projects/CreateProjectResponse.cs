namespace NexusAI.Api.Endpoints.Projects;

public sealed record CreateProjectResponse(
    Guid ProjectId,
    string Name);