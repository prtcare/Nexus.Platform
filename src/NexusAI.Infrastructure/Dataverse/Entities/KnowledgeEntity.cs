using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class KnowledgeEntity : DataverseEntity
{
    public Guid WorkspaceId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int Source { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}