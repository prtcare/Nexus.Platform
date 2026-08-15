using NexusAI.Domain.Project;

namespace NexusAI.Application.WorkItem;

public sealed record ListWorkItemsQuery(
    ProjectId ProjectId);