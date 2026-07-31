using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class AdrEntity : DataverseEntity
{
    public Guid KnowledgeId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Decision { get; set; } = string.Empty;

    public int Status { get; set; }

    public new DateTimeOffset CreatedAt { get; set; }
}