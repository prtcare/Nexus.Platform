using NexusAI.Domain.Common;
using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Project;

namespace NexusAI.Domain.WorkItem;

public interface IWorkItemRepository
    : IRepository<WorkItem, WorkItemId>
{
    Task<WorkItem?> GetByIdAsync(
        WorkItemId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkItem>> ListByProjectAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default);
}