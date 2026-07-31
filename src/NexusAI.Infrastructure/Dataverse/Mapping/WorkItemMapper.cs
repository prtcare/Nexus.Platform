using NexusAI.Domain.Project;
using NexusAI.Domain.WorkItem;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mappers;

public sealed class WorkItemMapper
    : IRepositoryMapper<WorkItem, WorkItemEntity>
{
    public WorkItemEntity ToEntity(WorkItem domain)
    {
        return new WorkItemEntity
        {
            Id = domain.Id.Value,
            ProjectId = domain.ProjectId.Value,
            Title = domain.Title,
            Description = domain.Description,
            Type = (int)domain.Type,
            Status = (int)domain.Status,
            CreatedAt = domain.CreatedAt
        };
    }

    public WorkItem ToDomain(WorkItemEntity entity)
    {
        var workItem = new WorkItem(
            new WorkItemId(entity.Id),
            new ProjectId(entity.ProjectId),
            entity.Title,
            (WorkItemType)entity.Type,
            entity.CreatedAt);

        workItem.UpdateDescription(entity.Description);
        workItem.ChangeStatus((WorkItemStatus)entity.Status);

        return workItem;
    }
}