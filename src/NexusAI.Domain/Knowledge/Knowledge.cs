using NexusAI.Domain.Common.Identifiers;

namespace NexusAI.Domain.Knowledge;

public sealed class Knowledge
{
    public Knowledge(
        KnowledgeId id,
        WorkspaceId workspaceId,
        string title,
        string content,
        KnowledgeSource source,
        DateTimeOffset createdAt)
    {
        Id = id;
        WorkspaceId = workspaceId;
        Title = title;
        Content = content;
        Source = source;
        CreatedAt = createdAt;
    }

    public KnowledgeId Id { get; }

    public WorkspaceId WorkspaceId { get; }

    public string Title { get; }

    public string Content { get; }

    public KnowledgeSource Source { get; }

    public DateTimeOffset CreatedAt { get; }
}