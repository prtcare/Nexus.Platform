using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class SnapshotEntity : DataverseEntity
{
    public Guid BranchId { get; set; }

    public string Description { get; set; } = string.Empty;

    public int Status { get; set; }

    public new DateTimeOffset CreatedAt { get; set; }
}