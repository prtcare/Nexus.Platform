namespace NexusAI.Application.Projects.Queries.ListProjects;

public sealed record ListProjectsResult(
    Guid ProjectId,
    string Name,
    DateTimeOffset CreatedAt);