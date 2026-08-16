using NexusAI.Domain.Conversation;
using NexusAI.Domain.Session;

namespace NexusAI.Application.Session.Queries.GetSession;

public sealed record GetSessionResult(
    SessionId SessionId,
    ConversationId ConversationId,
    SessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);