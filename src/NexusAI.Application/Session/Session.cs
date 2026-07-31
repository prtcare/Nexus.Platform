using NexusAI.Domain.Conversation;

namespace NexusAI.Domain.Session;

public sealed class Session
{
    public Session(
        SessionId id,
        ConversationId conversationId,
        SessionStatus status,
        DateTimeOffset startedAt)
    {
        Id = id;
        ConversationId = conversationId;
        Status = status;
        StartedAt = startedAt;
    }

    public SessionId Id { get; }

    public ConversationId ConversationId { get; }

    public SessionStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; }

    public void Complete()
    {
        Status = SessionStatus.Completed;
    }

    public void Cancel()
    {
        Status = SessionStatus.Cancelled;
    }
}