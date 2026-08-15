using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Conversations.Queries.ListConversations;

public sealed record ListConversationResult(
    ConversationId ConversationId,
    string Title,
    DateTimeOffset CreatedAt);