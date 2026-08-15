using NexusAI.Domain.Project;
using NexusAI.Domain.WorkItem;

namespace NexusAI.Application.WorkItem;

public sealed record ListWorkItemsResult(
    WorkItemId WorkItemId,
    ProjectId ProjectId,
    string Title,
    string? Description,
    WorkItemType Type,
    WorkItemStatus Status,
    DateTimeOffset CreatedAt);