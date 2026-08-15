namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class WorkspaceEntity : DataverseEntity
{
    public string Name { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int Status { get; set; }
}