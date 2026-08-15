using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Domain.Project;

public sealed class Project
{
    public Project(
        ProjectId id,
        WorkspaceId workspaceId,
        string name,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        WorkspaceId = workspaceId;
        Name = name.Trim();
        CreatedAt = createdAt;
        Status = ProjectStatus.Active;
    }

    public ProjectId Id { get; }

    public WorkspaceId WorkspaceId { get; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public ProjectStatus Status { get; private set; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
    }

    public void Archive()
    {
        Status = ProjectStatus.Archived;
    }
}