namespace NexusAI.Api.Endpoints.Artifacts;

public sealed record UpdateArtifactRequest(
    string Name,
    int Type,
    string Content);
