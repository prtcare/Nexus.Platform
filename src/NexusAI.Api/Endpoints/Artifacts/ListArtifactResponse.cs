namespace NexusAI.Api.Endpoints.Artifacts;

public sealed record ListArtifactResponse(
    Guid ArtifactId,
    string Name,
    int Type,
    DateTimeOffset CreatedAt);
