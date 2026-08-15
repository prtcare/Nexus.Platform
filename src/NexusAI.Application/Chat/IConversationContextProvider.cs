using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Chat;

public interface IConversationContextProvider
{
    Task<ConversationContext> GetAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default);
}