using NexusAI.Domain.Artifact;
using NexusAI.Domain.WorkItem;

namespace NexusAI.Application.Artifact.Queries.GetArtifact;

public sealed record GetArtifactResult(
    ArtifactId ArtifactId,
    WorkItemId WorkItemId,
    string Name,
    ArtifactType Type,
    string Content,
    DateTimeOffset CreatedAt);
