using NexusAI.Application.Chat.Commands.SendChat;
using NexusAI.Application.Providers;
using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Chat;

public sealed class ChatService : IChatService
{
    private readonly SendChatHandler _handler;
    private readonly IConversationContextProvider _contextProvider;

    public ChatService(
        SendChatHandler handler,
        IConversationContextProvider contextProvider)
    {
        _handler = handler;
        _contextProvider = contextProvider;
    }

    public async Task<SendChatResult> SendAsync(
    ConversationId conversationId,
    string prompt,
    CancellationToken cancellationToken = default)
    {
        var context =
            await _contextProvider.GetAsync(
                conversationId,
                cancellationToken);

        return await _handler.HandleAsync(
            new SendChatCommand(
    conversationId,
    prompt),
            cancellationToken);
    }
}