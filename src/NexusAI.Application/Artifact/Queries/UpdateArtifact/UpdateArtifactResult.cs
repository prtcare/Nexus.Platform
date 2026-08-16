using NexusAI.Domain.Artifact;

namespace NexusAI.Application.Artifact.Commands.UpdateArtifact;

public sealed record UpdateArtifactResult(
    ArtifactId ArtifactId,
    string Name,
    ArtifactType Type,
    string Content);
