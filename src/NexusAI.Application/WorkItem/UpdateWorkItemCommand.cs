using NexusAI.Domain.WorkItem;

public sealed record UpdateWorkItemCommand(
    WorkItemId WorkItemId,
    string Title,
    string? Description,
    WorkItemType Type,
    WorkItemStatus Status);