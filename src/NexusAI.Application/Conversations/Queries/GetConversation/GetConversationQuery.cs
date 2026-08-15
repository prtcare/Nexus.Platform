using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Conversations.Queries.GetConversation;

public sealed record GetConversationQuery(
    ConversationId ConversationId);