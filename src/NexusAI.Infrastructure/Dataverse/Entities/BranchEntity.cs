using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class BranchEntity : DataverseEntity
{
    public Guid ConversationId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}