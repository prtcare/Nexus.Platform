using NexusAI.Domain.Conversation;

namespace NexusAI.Application.ConversationMessages.Queries.GetConversationMessages;

public sealed record GetConversationMessagesQuery(
    ConversationId ConversationId);