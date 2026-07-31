using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Session.Commands;

public sealed record CreateSessionCommand(
    ConversationId ConversationId);