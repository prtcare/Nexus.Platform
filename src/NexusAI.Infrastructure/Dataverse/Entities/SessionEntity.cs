using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class SessionEntity : DataverseEntity
{
    public Guid ConversationId { get; set; }

    public int Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }
}