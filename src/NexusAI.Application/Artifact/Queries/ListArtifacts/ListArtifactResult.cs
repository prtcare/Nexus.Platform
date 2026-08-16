using NexusAI.Domain.Artifact;

namespace NexusAI.Application.Artifact.Queries.ListArtifacts;

public sealed record ListArtifactResult(
    ArtifactId ArtifactId,
    string Name,
    ArtifactType Type,
    DateTimeOffset CreatedAt);
