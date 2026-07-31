using NexusAI.Domain.Project;

namespace NexusAI.Domain.Conversation;

public sealed class Conversation
{
    public Conversation(
        ConversationId id,
        ProjectId projectId,
        string title,
        DateTimeOffset createdAt)
    {
        Id = id;
        ProjectId = projectId;
        Title = title;
        CreatedAt = createdAt;
        Status = ConversationStatus.Active;
    }

    public ConversationId Id { get; }

    public ProjectId ProjectId { get; }

    public string Title { get; }

    public DateTimeOffset CreatedAt { get; }

    public ConversationStatus Status { get; private set; }

    public void Archive()
    {
        Status = ConversationStatus.Archived;
    }
}