namespace NexusAI.Api.Endpoints.Artifacts;

public sealed record GetArtifactResponse(
    Guid ArtifactId,
    Guid WorkItemId,
    string Name,
    int Type,
    string Content,
    DateTimeOffset CreatedAt);
