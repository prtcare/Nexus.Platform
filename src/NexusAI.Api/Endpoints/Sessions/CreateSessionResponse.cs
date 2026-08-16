namespace NexusAI.Api.Endpoints.Sessions;

public sealed record CreateSessionResponse(
    Guid SessionId,
    int Status,
    DateTimeOffset StartedAt);