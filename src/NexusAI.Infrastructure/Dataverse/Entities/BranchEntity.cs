namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class BranchEntity : DataverseEntity
{
    public Guid ConversationId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int Status { get; set; }
}