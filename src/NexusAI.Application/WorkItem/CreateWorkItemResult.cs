using NexusAI.Domain.WorkItem;

namespace NexusAI.Application.WorkItem;

public sealed record CreateWorkItemResult(
    WorkItemId WorkItemId);