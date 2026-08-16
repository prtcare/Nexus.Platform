using NexusAI.Domain.Session;

namespace NexusAI.Application.Session.Commands;

public sealed record CreateSessionResult(
    SessionId SessionId,
    SessionStatus Status,
    DateTimeOffset StartedAt);