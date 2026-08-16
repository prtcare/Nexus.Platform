using NexusAI.Domain.Conversation;

namespace NexusAI.Application.Session.Queries.ListSessions;

public sealed record ListSessionsQuery(
    ConversationId ConversationId);