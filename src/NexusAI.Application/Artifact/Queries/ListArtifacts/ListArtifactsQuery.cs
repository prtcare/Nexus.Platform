using NexusAI.Domain.WorkItem;

namespace NexusAI.Application.Artifact.Queries.ListArtifacts;

public sealed record ListArtifactsQuery(
    WorkItemId WorkItemId);
