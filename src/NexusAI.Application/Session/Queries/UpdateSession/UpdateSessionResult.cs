using NexusAI.Domain.Session;

namespace NexusAI.Application.Session.Commands.UpdateSession;

public sealed record UpdateSessionResult(
    SessionId SessionId,
    SessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);