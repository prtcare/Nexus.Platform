using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Conversations.Commands.UpdateConversation;

public sealed record UpdateConversationResult(
    ConversationId ConversationId,
    string Title);