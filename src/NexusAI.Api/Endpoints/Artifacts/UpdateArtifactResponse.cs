namespace NexusAI.Api.Endpoints.Artifacts;

public sealed record UpdateArtifactResponse(
    Guid ArtifactId,
    string Name,
    int Type,
    string Content);
