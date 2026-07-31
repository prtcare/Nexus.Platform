using NexusAI.Domain.Project;
using NexusAI.Domain.WorkItem;

namespace NexusAI.Application.WorkItem;

public sealed record CreateWorkItemCommand(
    ProjectId ProjectId,
    string Title,
    WorkItemType Type);