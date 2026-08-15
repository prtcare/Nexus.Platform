namespace NexusAI.Application.Chat;

using NexusAI.Application.Chat.Commands.SendChat;
using NexusAI.Domain.Conversation;

public interface IChatService
{
    Task<SendChatResult> SendAsync(
        ConversationId conversationId,
        string prompt,
        CancellationToken cancellationToken = default);
}