namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class SnapshotEntity : DataverseEntity
{
    public Guid BranchId { get; set; }

    public Guid ConversationId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;
}