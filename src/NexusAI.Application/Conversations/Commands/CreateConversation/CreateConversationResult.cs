using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Conversations.Commands.CreateConversation;

public sealed record CreateConversationResult(
    ConversationId ConversationId,
    string Title);