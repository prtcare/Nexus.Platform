using NexusAI.Domain.Session;

namespace NexusAI.Application.Session.Queries.GetSession;

public sealed record GetSessionQuery(
    SessionId SessionId);