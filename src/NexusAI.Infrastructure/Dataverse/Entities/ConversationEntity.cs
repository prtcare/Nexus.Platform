namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class ConversationEntity : DataverseEntity
{
    public Guid ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}