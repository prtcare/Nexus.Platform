using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Project;
using NexusAI.Domain.WorkItem;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Mapping;

public sealed class WorkItemMapper
    : IRepositoryMapper<WorkItem, WorkItemEntity>
{
    private const int DataverseTask = 121930000;
    private const int DataverseBug = 121930001;
    private const int DataverseFeature = 121930002;
    private const int DataverseEpic = 121930003;
    private const int DataverseStory = 121930004;
    private const int DataverseResearch = 121930005;
    private const int DataverseIdea = 121930006;
    private const int DataverseSpike = 121930007;

    private const int DataverseNew = 121930000;
    private const int DataverseActive = 121930001;
    private const int DataverseBlocked = 121930002;
    private const int DataverseCompleted = 121930003;
    private const int DataverseCancelled = 121930004;

    public WorkItemEntity ToEntity(WorkItem workItem)
    {
        return new WorkItemEntity
        {
            Id = workItem.Id.Value,
            ProjectId = workItem.ProjectId.Value,
            Title = workItem.Title,
            Description = workItem.Description ?? string.Empty,
            Type = ToDataverseType(workItem.Type),
            Status = ToDataverseStatus(workItem.Status),
            CreatedAt = workItem.CreatedAt
        };
    }

    public WorkItem ToDomain(WorkItemEntity entity)
    {
        var workItem = new WorkItem(
            new WorkItemId(entity.Id),
            new ProjectId(entity.ProjectId),
            entity.Title,
            FromDataverseType(entity.Type),
            entity.CreatedAt);

        workItem.UpdateDescription(
            string.IsNullOrWhiteSpace(entity.Description)
                ? null
                : entity.Description);

        workItem.ChangeStatus(
            FromDataverseStatus(entity.Status));

        return workItem;
    }

    private static int ToDataverseType(WorkItemType type)
    {
        return type switch
        {
            WorkItemType.Task => DataverseTask,
            WorkItemType.Bug => DataverseBug,
            WorkItemType.Feature => DataverseFeature,
            WorkItemType.Epic => DataverseEpic,
            WorkItemType.Story => DataverseStory,
            WorkItemType.Research => DataverseResearch,
            WorkItemType.Idea => DataverseIdea,
            WorkItemType.Spike => DataverseSpike,

            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Unsupported WorkItemType.")
        };
    }

    private static WorkItemType FromDataverseType(int value)
    {
        return value switch
        {
            DataverseTask => WorkItemType.Task,
            DataverseBug => WorkItemType.Bug,
            DataverseFeature => WorkItemType.Feature,
            DataverseEpic => WorkItemType.Epic,
            DataverseStory => WorkItemType.Story,
            DataverseResearch => WorkItemType.Research,
            DataverseIdea => WorkItemType.Idea,
            DataverseSpike => WorkItemType.Spike,

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported Dataverse WorkItem Type value.")
        };
    }

    private static int ToDataverseStatus(WorkItemStatus status)
    {
        return status switch
        {
            WorkItemStatus.New => DataverseNew,
            WorkItemStatus.Active => DataverseActive,
            WorkItemStatus.Blocked => DataverseBlocked,
            WorkItemStatus.Completed => DataverseCompleted,
            WorkItemStatus.Cancelled => DataverseCancelled,

            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unsupported WorkItemStatus.")
        };
    }

    private static WorkItemStatus FromDataverseStatus(int value)
    {
        return value switch
        {
            // 0 is DataverseContext's sentinel for a missing/unset
            // du_workitemstatus attribute, not a real OptionSet code -
            // treat it as New, the status every work item is created with.
            0 => WorkItemStatus.New,

            DataverseNew => WorkItemStatus.New,
            DataverseActive => WorkItemStatus.Active,
            DataverseBlocked => WorkItemStatus.Blocked,
            DataverseCompleted => WorkItemStatus.Completed,
            DataverseCancelled => WorkItemStatus.Cancelled,

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported Dataverse WorkItem Status value.")
        };
    }
}