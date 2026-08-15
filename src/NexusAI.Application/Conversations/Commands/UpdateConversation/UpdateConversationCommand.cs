using NexusAI.Domain.Common.Identifiers;
using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Conversations.Commands.UpdateConversation;

public sealed record UpdateConversationCommand(
    ConversationId ConversationId,
    string Title,
    string Description,
    ConversationType Type,
    ConversationVisibility Visibility);