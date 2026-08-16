namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class SessionEntity : DataverseEntity
{
    public Guid ConversationId { get; set; }

    public int Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }
}