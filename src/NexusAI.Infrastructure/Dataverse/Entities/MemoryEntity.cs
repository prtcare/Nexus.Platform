namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class MemoryEntity : DataverseEntity
{
    public Guid WorkspaceId { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? ConversationId { get; set; }

    public int Type { get; set; }

    public int Source { get; set; }

    public string Content { get; set; } = string.Empty;

    public string Keywords { get; set; } = string.Empty;

    public string Metadata { get; set; } = string.Empty;
}