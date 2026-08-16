using NexusAI.Domain.Common;
using NexusAI.Domain.Conversation;

namespace NexusAI.Domain.Session;

public interface ISessionRepository
    : IRepository<Session, SessionId>
{
    Task<IReadOnlyList<Session>> ListByConversationAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default);
}