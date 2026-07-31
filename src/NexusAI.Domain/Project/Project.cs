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
        Id = id;
        WorkspaceId = workspaceId;
        Name = name;
        CreatedAt = createdAt;
        Status = ProjectStatus.Active;
    }

    public ProjectId Id { get; }

    public WorkspaceId WorkspaceId { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAt { get; }

    public ProjectStatus Status { get; private set; }

    public void Archive()
    {
        Status = ProjectStatus.Archived;
    }
}