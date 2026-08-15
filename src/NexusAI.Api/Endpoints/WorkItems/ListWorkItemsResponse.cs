namespace NexusAI.Api.Endpoints.WorkItem;

public sealed record ListWorkItemsResponse(
    Guid WorkItemId,
    Guid ProjectId,
    string Title,
    string? Description,
    int Type,
    int Status,
    DateTimeOffset CreatedAt);