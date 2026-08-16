using NexusAI.Domain.Project;

namespace NexusAI.Domain.WorkItem;

public sealed class WorkItem
{
    public WorkItemId Id { get; }
    public ProjectId ProjectId { get; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public WorkItemType Type { get; private set; }
    public WorkItemStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }

    public WorkItem(
        WorkItemId id,
        ProjectId projectId,
        string title,
        WorkItemType type,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Id = id;
        ProjectId = projectId;
        Title = title.Trim();
        Type = type;
        Status = WorkItemStatus.New;
        CreatedAt = createdAt;
    }

    public void UpdateTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Title = title.Trim();
    }

    public void UpdateDescription(string? description)
    {
        Description = description;
    }
    public void ChangeType(WorkItemType type)
    {
        Type = type;
    }
    public void ChangeStatus(WorkItemStatus status)
    {
        Status = status;
    }
}