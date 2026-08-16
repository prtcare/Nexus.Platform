using NexusAI.Domain.Session;

namespace NexusAI.Application.Session.Queries.ListSessions;

public sealed record ListSessionResult(
    SessionId SessionId,
    SessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);