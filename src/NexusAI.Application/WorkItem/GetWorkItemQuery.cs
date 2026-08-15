using NexusAI.Domain.WorkItem;

namespace NexusAI.Application.WorkItem;

public sealed record GetWorkItemQuery(
    WorkItemId WorkItemId);