using NexusAI.Domain.Common;
using NexusAI.Domain.WorkItem;

namespace NexusAI.Domain.Artifact;

public sealed class Artifact : Entity<ArtifactId>
{
    public Artifact(
        ArtifactId id,
        WorkItemId workItemId,
        string name,
        ArtifactType type,
        string content,
        DateTimeOffset createdAt)
        : base(id)
    {
        WorkItemId = workItemId;
        Name = name;
        Type = type;
        Content = content;
        CreatedAt = createdAt;
    }

    public WorkItemId WorkItemId { get; }

    public string Name { get; private set; }

    public ArtifactType Type { get; private set; }

    public string Content { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public void Rename(string name)
    {
        Name = name;
    }

    public void UpdateContent(string content)
    {
        Content = content;
    }

    public void ChangeType(ArtifactType type)
    {
        Type = type;
    }
}