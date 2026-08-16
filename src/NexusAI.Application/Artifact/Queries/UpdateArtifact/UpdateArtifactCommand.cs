using NexusAI.Domain.Artifact;

namespace NexusAI.Application.Artifact.Commands.UpdateArtifact;

public sealed record UpdateArtifactCommand(
    ArtifactId ArtifactId,
    string Name,
    ArtifactType Type,
    string Content);
