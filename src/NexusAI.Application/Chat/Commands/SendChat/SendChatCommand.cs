using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Chat.Commands.SendChat;

public sealed record SendChatCommand(
    ConversationId ConversationId,
    string Prompt);