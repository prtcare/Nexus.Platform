using NexusAI.Infrastructure.Dataverse.Entities;

namespace NexusAI.Infrastructure.Dataverse.Entities;

public sealed class ArtifactEntity : DataverseEntity
{
    public Guid WorkItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Type { get; set; }

    public string Content { get; set; } = string.Empty;
}