using NexusAI.Domain.Common;
using NexusAI.Domain.Conversation;

namespace NexusAI.Domain.Session;

public sealed class Session : Entity<SessionId>
{
    public Session(
        SessionId id,
        ConversationId conversationId,
        SessionStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt = null)
        : base(id)
    {
        ConversationId = conversationId;
        Status = status;
        StartedAt = startedAt;
        EndedAt = endedAt;
    }

    public ConversationId ConversationId { get; }

    public SessionStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? EndedAt { get; private set; }

    public void End(DateTimeOffset endedAt)
    {
        if (Status == SessionStatus.Ended)
        {
            return;
        }

        if (endedAt < StartedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endedAt),
                endedAt,
                "Session end time cannot be earlier than the start time.");
        }

        Status = SessionStatus.Ended;
        EndedAt = endedAt;
    }
}