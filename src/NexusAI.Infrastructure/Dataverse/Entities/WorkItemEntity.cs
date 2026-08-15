namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class WorkItemEntity : DataverseEntity
{
    public Guid ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int Type { get; set; }

    public int Status { get; set; }

    public int Priority { get; set; }
}