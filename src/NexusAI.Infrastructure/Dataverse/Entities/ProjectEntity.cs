namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class ProjectEntity : DataverseEntity
{
    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}