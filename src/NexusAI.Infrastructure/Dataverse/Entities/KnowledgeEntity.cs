namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class KnowledgeEntity : DataverseEntity
{
    public Guid WorkspaceId { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? SourceConversationId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string? Keywords { get; set; }

    public int Type { get; set; }

    public int Status { get; set; }
}