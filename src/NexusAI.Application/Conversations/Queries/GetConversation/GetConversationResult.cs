using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Conversations.Queries.GetConversation;

public sealed record GetConversationResult(
    ConversationId ConversationId,
    string Title,
    DateTimeOffset CreatedAt);