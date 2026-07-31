using NexusAI.Domain.Common;
using NexusAI.Infrastructure.Dataverse.Common;

namespace NexusAI.Domain.WorkItem;

public interface IWorkItemRepository
    : IRepository<WorkItem, WorkItemId>
{
}