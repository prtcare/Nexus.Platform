namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class WorkspaceEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}