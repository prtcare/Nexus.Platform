using NexusAI.Domain.Artifact;
using NexusAI.Domain.WorkItem;

namespace NexusAI.Application.Artifact.Commands;

public sealed record CreateArtifactCommand(
    WorkItemId WorkItemId,
    string Name,
    ArtifactType Type,
    string Content);