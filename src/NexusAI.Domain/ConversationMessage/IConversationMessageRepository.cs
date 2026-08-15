using NexusAI.Domain.Conversation;

namespace NexusAI.Domain.ConversationMessage;

public interface IConversationMessageRepository
{
    Task AddAsync(
        ConversationMessage message,
        CancellationToken cancellationToken = default);

    Task<ConversationMessage?> GetAsync(
        ConversationMessageId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationMessage>> ListByConversationAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default);
}