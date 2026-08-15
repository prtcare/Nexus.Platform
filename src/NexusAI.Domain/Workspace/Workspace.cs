using NexusAI.Domain.Common;
using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Domain.Workspace;

public sealed class Workspace : AggregateRoot<WorkspaceId>
{
    public Workspace(
        WorkspaceId id,
        string name,
        string owner,
        string description,
        DateTimeOffset createdAt)
        : base(id)
    {
        Rename(name);
        ChangeOwner(owner);
        ChangeDescription(description);

        CreatedAt = createdAt;
        Status = WorkspaceStatus.Active;
    }

    public string Name { get; private set; } = string.Empty;

    public string Owner { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public WorkspaceStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
    }

    public void ChangeOwner(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        Owner = owner.Trim();
    }

    public void ChangeDescription(string description)
    {
        Description = description?.Trim() ?? string.Empty;
    }

    public void Archive()
    {
        Status = WorkspaceStatus.Archived;
    }
}