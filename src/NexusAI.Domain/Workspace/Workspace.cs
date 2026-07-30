using NexusAI.Domain.Common;
using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Domain.Workspace;

public sealed class Workspace : AggregateRoot<WorkspaceId>
{
    public Workspace(
        WorkspaceId id,
        string name,
        DateTimeOffset createdAt)
        : base(id)
    {
        Rename(name);

        CreatedAt = createdAt;
        Status = WorkspaceStatus.Active;
    }

    public string Name { get; private set; } = string.Empty;

    public WorkspaceStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
    }

    public void Archive()
    {
        Status = WorkspaceStatus.Archived;
    }
}