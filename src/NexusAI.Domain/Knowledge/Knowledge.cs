using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Domain.Knowledge;

public sealed class Knowledge
{
    public Knowledge(
        KnowledgeId id,
        WorkspaceId workspaceId,
        string title,
        string content,
        KnowledgeType type,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        Id = id;
        WorkspaceId = workspaceId;
        Title = title.Trim();
        Content = content.Trim();
        Type = type;
        Status = KnowledgeStatus.Active;
        CreatedAt = createdAt;
    }

    public KnowledgeId Id { get; }

    public WorkspaceId WorkspaceId { get; }

    public string Title { get; private set; }

    public string Content { get; private set; }

    public KnowledgeType Type { get; private set; }

    public KnowledgeStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public void ChangeTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title.Trim();
    }

    public void ChangeContent(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        Content = content.Trim();
    }

    public void ChangeType(KnowledgeType type)
    {
        Type = type;
    }

    public void ChangeStatus(KnowledgeStatus status)
    {
        Status = status;
    }

    public void Archive()
    {
        Status = KnowledgeStatus.Archived;
    }
}