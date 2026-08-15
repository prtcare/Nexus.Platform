namespace NexusAI.Api.Endpoints.Snapshots;

public sealed record ListSnapshotResponse(
    Guid SnapshotId,
    string Name,
    string State,
    DateTimeOffset CreatedAt);