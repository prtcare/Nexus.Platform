namespace NexusAI.Api.Endpoints.Artifacts;

public sealed record CreateArtifactRequest(
    string Name,
    int Type,
    string Content);
