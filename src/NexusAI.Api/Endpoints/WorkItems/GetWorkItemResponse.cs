namespace NexusAI.Api.Endpoints.WorkItems;

public sealed record GetWorkItemResponse(
    Guid WorkItemId,
    Guid ProjectId,
    string Title,
    string Description,
    int Type,
    int Status,
    DateTimeOffset CreatedAt);