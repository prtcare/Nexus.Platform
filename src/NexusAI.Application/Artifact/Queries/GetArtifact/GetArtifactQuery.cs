using NexusAI.Domain.Artifact;

namespace NexusAI.Application.Artifact.Queries.GetArtifact;

public sealed record GetArtifactQuery(
    ArtifactId ArtifactId);
